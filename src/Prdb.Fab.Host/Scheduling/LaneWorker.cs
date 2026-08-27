using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Host.Scheduling;

/// <summary>
/// One lane, turning. ADR 0038: one hosted service per lane rather than a
/// semaphore over many, because a semaphore serialises but cannot take turns
/// and ADR 0032 requires turns.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0038 left two things here to be settled by writing them. The first is
/// how the loop waits when nothing is due, and at what resolution due-ness is
/// read: it is a <see cref="PeriodicTimer"/> at a fixed one-second resolution,
/// the same for every lane, and every tick asks the store rather than
/// computing a sleep from the nearest due time. Computing the sleep would be
/// cheaper and would put a second copy of "what is due" inside the worker —
/// and ADR 0038 gave that job to the row, precisely so that <em>run now</em>
/// stays one write. One indexed query a second is not worth a second truth.
/// </para>
/// <para>
/// The second is what happens when a run throws, which ADR 0043 answered: the
/// exception is that run's <em>failed</em> outcome, so three of them are
/// ADR 0014's Gap and the failure reaches ADR 0018's page through a mechanism
/// that already exists. The loop itself only ever ends on cancellation, which
/// is what makes a dead lane — a thing ADR 0018 cannot draw — impossible here.
/// </para>
/// </remarks>
internal sealed class LaneWorker(
    Lane lane,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LaneWorker> logger) : BackgroundService
{
    /// <summary>
    /// How often a lane looks. Not a cadence — the cadences are the routines'
    /// own, and this is only the grain they are read at.
    /// </summary>
    public static readonly TimeSpan Resolution = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("The {Lane} lane is turning.", lane);

        using var timer = new PeriodicTimer(Resolution, time);

        try
        {
            do
            {
                await TurnAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("The {Lane} lane stopped: the tool is shutting down.", lane);
        }
    }

    private async Task TurnAsync(CancellationToken stoppingToken)
    {
        // A scope per turn, not per lane: ADR 0039 wants short-lived contexts,
        // and a context held for the life of the container would hold a
        // connection with it.
        await using var scope = scopes.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IRoutineStore>();
        var routines = scope.ServiceProvider.GetServices<IRoutine>().ToDictionary(routine => routine.Name);

        var due = await store.DueAsync(lane, stoppingToken);

        foreach (var row in due)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!routines.TryGetValue(row.Name, out var routine))
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

            await RunAsync(store, routine, row, stoppingToken);
        }
    }

    private async Task RunAsync(
        IRoutineStore store,
        IRoutine routine,
        DueRoutine row,
        CancellationToken stoppingToken)
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
        catch (Exception exception)
        {
            // ADR 0043: an exception escaping a run is that run's failed
            // outcome. It is caught here rather than allowed to leave
            // ExecuteAsync, because a lane that stops is not one of the
            // conditions ADR 0018 can draw — it presents as a tool where
            // nothing happens and nothing is wrong.
            logger.LogError(
                exception,
                "The routine {Name} failed in the {Lane} lane.",
                routine.Name,
                lane);

            result = RunResult.Failed(exception.GetType().Name + ": " + exception.Message);
        }

        await store.RecordAsync(row.Id, result, routine.Cadence, stoppingToken);
    }
}
