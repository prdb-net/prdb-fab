using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Scheduling;

/// <summary>
/// The routine row, read and written. ADR 0038 made this row the only truth
/// about what is due, which is what keeps <em>run now</em> one act.
/// </summary>
public sealed class RoutineStore(
    FabDbContext context,
    TimeProvider time,
    ILogger<RoutineStore> logger) : IRoutineStore
{
    /// <summary>
    /// ADR 0014 keeps the last fifty runs of a routine, so the log is something
    /// a person reads rather than something that grows for as long as the
    /// container runs.
    /// </summary>
    public const int RunsKeptPerRoutine = 50;

    public async Task<IReadOnlyList<DueRoutine>> DueAsync(Lane lane, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        return await context.Routines
            .Where(row => row.Lane == lane && row.DueAt <= now)
            .OrderBy(row => row.DueAt)
            .Select(row => new DueRoutine(row.Id, row.Name, row.Target))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAsync(
        long routineId,
        RunResult result,
        TimeSpan cadence,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // Tracked deliberately: the context reads untracked by default (ADR
        // 0039), and this is the one place that writes the row back.
        var routine = await context.Routines
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == routineId, cancellationToken);

        if (routine is null)
        {
            // The row was removed while the run was in flight. Nothing to write
            // it against, and nothing broken — the next tick simply will not
            // find it either.
            logger.LogDebug("Routine {RoutineId} no longer exists; its run is not recorded.", routineId);
            return;
        }

        switch (result.Outcome)
        {
            case RunOutcome.Succeeded:
                routine.LastSuccessAt = now;
                routine.ConsecutiveFailures = 0;
                break;

            case RunOutcome.Failed:
                routine.LastFailureAt = now;
                routine.ConsecutiveFailures++;
                break;

            case RunOutcome.Interrupted:
                // ADR 0038: neither a success nor a failure, and it moves no
                // counter. A restart is not evidence that the routine is broken.
                break;

            case null:
                // ADR 0032, and the only place it is applied: an empty tick is
                // not a run. The due time moves on so the lane does not spin,
                // and nothing is written to the log. ADR 0014's deferral is the
                // same absence with a wait of its own, which is why it is the
                // same case here.
                break;
        }

        if (result.Outcome is null && result.DueIn is { } wait)
        {
            routine.LastDeferredAt = now;
            routine.DeferredUntil = now + wait;
            routine.LastDeferredReason = result.Reason ?? "The routine is waiting for a governing limit.";
        }
        else if (result.Outcome is not null)
        {
            routine.DeferredUntil = null;
            routine.LastDeferredReason = null;
        }

        if (routine.RunNowPending)
        {
            routine.RunNowPending = false;
            if (result.Outcome is null && result.DueIn is not null)
            {
                routine.LastRunNowOutcome = RunNowOutcome.Deferred;
                routine.LastRunNowDetail = result.Reason ?? "The scheduler deferred the routine.";
            }
            else if (result.Outcome is null)
            {
                routine.LastRunNowOutcome = RunNowOutcome.Refused;
                routine.LastRunNowDetail = "The scheduler found no work to do.";
            }
            else
            {
                routine.LastRunNowOutcome = RunNowOutcome.Accepted;
                routine.LastRunNowDetail = "The scheduler completed the requested turn.";
            }
        }

        routine.DueAt = now + NextDueIn(result, routine.ConsecutiveFailures, cadence);

        if (result.IsRecorded)
        {
            context.RoutineRuns.Add(new RoutineRunRow
            {
                RoutineId = routine.Id,
                StartedAt = now,
                FinishedAt = now,
                Outcome = result.Outcome!.Value,
                ItemsHandled = result.ItemsHandled,
                ResultsSeen = result.ResultsSeen,
                RowsAdded = result.RowsAdded,
                Reason = result.Reason,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        if (result.IsRecorded)
        {
            await TrimRunLogAsync(routine.Id, cancellationToken);
        }
    }

    /// <summary>
    /// How long until this routine may be due again.
    /// </summary>
    /// <remarks>
    /// Three answers in one place, in the order they override each other. What
    /// the run itself knew wins — a deferral waiting on the budget, or the
    /// <c>Retry-After</c> ADR 0014 says overrides the backoff <em>exactly</em>.
    /// Otherwise a failure backs off, doubling per consecutive failure and
    /// capped at an hour. Otherwise the routine's own cadence, which for the
    /// work-set routines is ADR 0032's idle tick rather than an interval.
    /// </remarks>
    private static TimeSpan NextDueIn(RunResult result, int consecutiveFailures, TimeSpan cadence) =>
        result.DueIn
        ?? (result.Outcome == RunOutcome.Failed
            ? Backoff.After(cadence, consecutiveFailures)
            : cadence);

    public async Task<bool> RunNowAsync(string name, string? target, CancellationToken cancellationToken) =>
        (await RunNowDetailedAsync(name, target, cancellationToken)).Accepted;

    public async Task<RunNowVerdict> RunNowDetailedAsync(string name, string? target, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        const string acceptedDetail = "The routine is due now and will use its ordinary scheduler path.";
        var accepted = await context.Routines
            .Where(row => row.Name == name && row.Target == target && !row.RunNowPending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.DueAt, now)
                .SetProperty(row => row.LastRunNowAt, now)
                .SetProperty(row => row.LastRunNowOutcome, RunNowOutcome.Accepted)
                .SetProperty(row => row.LastRunNowDetail, acceptedDetail)
                .SetProperty(row => row.RunNowPending, true), cancellationToken);
        if (accepted > 0)
        {
            return new RunNowVerdict(RunNowOutcome.Accepted, acceptedDetail);
        }

        if (!await context.Routines.AnyAsync(
                row => row.Name == name && row.Target == target,
                cancellationToken))
        {
            return new RunNowVerdict(RunNowOutcome.Refused, "There is no schedule row for that routine.");
        }

        const string deferredDetail = "A requested turn is already waiting for its lane.";
        await context.Routines
            .Where(row => row.Name == name && row.Target == target && row.RunNowPending)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.LastRunNowAt, now)
                .SetProperty(row => row.LastRunNowOutcome, RunNowOutcome.Deferred)
                .SetProperty(row => row.LastRunNowDetail, deferredDetail), cancellationToken);
        return new RunNowVerdict(RunNowOutcome.Deferred, deferredDetail);
    }

    public async Task<bool> RetireAsync(string name, string? target, CancellationToken cancellationToken)
    {
        // The run log goes with it, on the cascade ADR 0033 declared. That is
        // the honest reading of ADR 0014's "bootstrap is not a state of the
        // application": what retired is not a routine with an empty history, it
        // is not a routine, and a run log of something that no longer exists is
        // a row nobody can ask a question about.
        var removed = await context.Routines
            .Where(row => row.Name == name && row.Target == target)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            logger.LogInformation("The routine {Name} has finished, and its row is gone.", name);
        }

        return removed > 0;
    }

    private async Task TrimRunLogAsync(long routineId, CancellationToken cancellationToken)
    {
        var cutoff = await context.RoutineRuns
            .Where(row => row.RoutineId == routineId)
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Id)
            .Skip(RunsKeptPerRoutine - 1)
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (cutoff is null)
        {
            return;
        }

        await context.RoutineRuns
            .Where(row => row.RoutineId == routineId && row.Id < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
