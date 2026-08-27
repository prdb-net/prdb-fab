using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Scheduling;

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
/// <para>
/// What a run <em>was</em> is <see cref="RoutineRunner"/>'s, one layer down.
/// This class is the timing and nothing else, which is why it can be read in
/// one screen and why the four outcomes can be tested without hosting anything.
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

        await scope.ServiceProvider
            .GetRequiredService<RoutineRunner>()
            .TurnAsync(lane, stoppingToken);
    }
}
