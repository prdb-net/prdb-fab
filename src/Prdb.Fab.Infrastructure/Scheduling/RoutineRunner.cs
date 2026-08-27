using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;

namespace Prdb.Fab.Infrastructure.Scheduling;

/// <summary>
/// One turn of a lane: ask what is due, run it, and decide what the run was.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the worker that turns it, because these are two different
/// jobs. The worker is timing — a timer, a scope per tick, a loop that only
/// ends on cancellation. This is the part with the rules in it, and there are
/// now four of them: a routine that did its work, one that was interrupted by a
/// restart (ADR 0038), one that threw and is therefore a failure (ADR 0043),
/// and one the governor turned away, which is none of the three (ADR 0014).
/// </para>
/// <para>
/// Each is expressed once, here, which is what ADR 0038 asked for and what
/// keeps a routine from having to remember any of it.
/// </para>
/// </remarks>
public sealed class RoutineRunner(
    IRoutineStore store,
    IEnumerable<IRoutine> routines,
    ILogger<RoutineRunner> logger)
{
    /// <summary>Runs everything <paramref name="lane"/> has due.</summary>
    public async Task TurnAsync(Lane lane, CancellationToken stoppingToken)
    {
        var known = routines.ToDictionary(routine => routine.Name);

        foreach (var row in await store.DueAsync(lane, stoppingToken))
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!known.TryGetValue(row.Name, out var routine))
            {
                // A row naming code this build does not have. It happens on a
                // downgrade, which ADR 0044 says is unsupported — so this is a
                // warning rather than a crash, and the row is left alone so
                // that a build which does know the name picks it up again.
                logger.LogWarning(
                    "Routine row {RoutineId} names {Name}, which this build does not have. Skipping it.",
                    row.Id,
                    row.Name);
                continue;
            }

            await RunAsync(routine, row, stoppingToken);
        }
    }

    private async Task RunAsync(IRoutine routine, DueRoutine row, CancellationToken stoppingToken)
    {
        RunResult result;

        try
        {
            result = await routine.RunAsync(row.Target, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // ADR 0038's third outcome. Recorded, because a long run cut short
            // by a restart is exactly what somebody goes to the run log to find,
            // and it moves no counter because a restart says nothing about
            // whether the routine works.
            //
            // Written with a token that is not the one that just fired: the
            // record is the point of catching this, and cancelling it too would
            // lose the only trace.
            await store.RecordAsync(row.Id, RunResult.Interrupted(0), routine.Cadence, CancellationToken.None);
            throw;
        }
        catch (PrdbDeferredException deferred)
        {
            // ADR 0014's deferral. Not a failure and not a run: the tool is
            // working exactly as designed, which is the distinction ADR 0018
            // later draws as a Brake against a Gap. The routine comes back when
            // the budget says so, and loses nothing by it — every one of them
            // works from a work set rather than from a position (ADR 0032).
            logger.LogDebug(
                "The routine {Name} was deferred for {Seconds}s: {Deferral}",
                routine.Name,
                (int)deferred.Wait.TotalSeconds,
                deferred.Deferral);

            result = RunResult.Deferred(deferred.Wait);
        }
        catch (Exception exception)
        {
            // ADR 0043: an exception escaping a run is that run's failed
            // outcome. It is caught rather than allowed to leave the lane,
            // because a lane that stops is not one of the conditions ADR 0018
            // can draw — it presents as a tool where nothing happens and
            // nothing is wrong.
            logger.LogError(
                exception,
                "The routine {Name} failed.",
                routine.Name);

            result = RunResult.Failed(exception.GetType().Name + ": " + exception.Message);
        }

        await store.RecordAsync(row.Id, result, routine.Cadence, stoppingToken);
    }
}
