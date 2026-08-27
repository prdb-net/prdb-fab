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

    public async Task<bool> RunNowAsync(string name, string? target, CancellationToken cancellationToken)
    {
        var updated = await context.Routines
            .Where(row => row.Name == name && row.Target == target)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.DueAt, time.GetUtcNow()),
                cancellationToken);

        return updated > 0;
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
