using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// One page of one change feed, per run.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014's cadence is how often to <em>look</em>, and one page is what a run
/// takes however far behind the feed is. A run that drained a backlog would hold
/// the lane for as long as the backlog lasted, which is the shape ADR 0032
/// refused: a bounded run yields, and being behind is answered by coming round
/// again rather than by not stopping. A page is a thousand rows, so the slowest
/// feed here still carries twenty-four thousand changes a day.
/// </para>
/// <para>
/// The bootstrap is the exception, and it is a routine of its own rather than a
/// state of this one (ADR 0014) — see <see cref="ActorDrainRoutine"/>, which
/// turns the same feed at the idle tick until it is caught up and then retires.
/// </para>
/// <para>
/// Nothing is caught here. A refusal is the lane's to read as a failure and a
/// deferral is the lane's to read as neither (ADR 0038, ADR 0014), and both of
/// those decisions are made once, in <c>RoutineRunner</c>.
/// </para>
/// </remarks>
public abstract class ChangeFeedRoutine(
    ChangeFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    ILogger logger) : IRoutine
{
    /// <summary>
    /// prdb's largest page. The feeds are documented at a default of a hundred
    /// and a maximum of a thousand, and the whole request costs one against the
    /// rate limit either way — so anything smaller spends ten requests where one
    /// would do.
    /// </summary>
    public const int APage = 1000;

    /// <summary>
    /// What a feed that starts at where prdb is now asks for on its first run.
    /// </summary>
    /// <remarks>
    /// One row, because the request is not for the rows: it is for the
    /// <c>serverTimeUtc</c> the answer carries, which is the only lower bound
    /// prdb will later read back as its own. This tool's clock is explicitly not
    /// a substitute, and there is no endpoint that hands out the server's time
    /// on its own.
    /// </remarks>
    private const int JustTheClock = 1;

    /// <summary>What this routine turns. See <see cref="ChangeFeed"/>.</summary>
    protected ChangeFeed Source { get; } = feed;

    protected FeedCursors Cursors { get; } = cursors;

    protected FabDbContext Context { get; } = context;

    protected ILogger Logger { get; } = logger;

    public abstract string Name { get; }

    public abstract TimeSpan Cadence { get; }

    /// <summary>ADR 0014 puts the prdb feeds in the sync lane.</summary>
    public virtual Lane Lane => Lane.Sync;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var apiKey = await Context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // ADR 0010 makes the prdb key mandatory and ADR 0013 has every one
            // of these feeds belong to it. Before onboarding has reached that
            // step there is no work here rather than a failure — nothing is
            // broken, the tool has not been told anything yet.
            return RunResult.NothingToDo;
        }

        var from = await Cursors.PositionAsync(Source.Feed, cancellationToken);
        var takingTheClock = from is null && !Source.StartsAtTheBeginning;

        var page = await Source.ReadAsync(
            apiKey,
            from,
            takingTheClock ? JustTheClock : APage,
            cancellationToken);

        var next = takingTheClock ? StartingNow(page) : page.Next();

        if (next is null)
        {
            // prdb answered without anything a position can be built from. The
            // rows that did arrive are applied — they are upserts — and the
            // position stays where it was, which is where the next run asks
            // from again.
            Logger.LogDebug("The {Feed} feed answered with nothing to move its cursor by.", Source.Feed);

            return RunResult.Handled(page.Applied);
        }

        await Cursors.SaveAsync(Source.Feed, next, cancellationToken);

        Logger.LogDebug(
            "The {Feed} feed applied {Count} change(s) and stands at {Position}.",
            Source.Feed,
            page.Applied,
            next.At);

        await ReachedAsync(next, cancellationToken);

        return RunResult.Handled(page.Applied);
    }

    /// <summary>
    /// What this routine does once a page has been applied and the position
    /// saved. Nothing, for a routine that recurs.
    /// </summary>
    protected virtual Task ReachedAsync(FeedPosition position, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// The position a feed that does not walk history starts from: prdb's own
    /// clock, and nothing before it.
    /// </summary>
    /// <remarks>
    /// The images feed is global and the catalogue is a fraction of it, so
    /// draining prdb's image corpus would be the most expensive thing this tool
    /// ever did and would discard almost all of it (ADR 0013). What the feed is
    /// for is artwork that arrives days after the video, and every catalogue row
    /// there will ever be is written after this moment by a detail read that
    /// brings <c>images[]</c> with it — so history holds nothing this
    /// installation can place, and starting at now loses none of it.
    /// </remarks>
    private FeedPosition? StartingNow(FeedPage page)
    {
        if (page.ServerTimeUtc is not { } clock)
        {
            Logger.LogDebug(
                "The {Feed} feed did not report a server time, so it has nowhere to start yet.",
                Source.Feed);

            return null;
        }

        Logger.LogInformation(
            "The {Feed} feed starts at what prdb has now rather than at what it has ever had.",
            Source.Feed);

        return FeedPosition.CaughtUpAt(clock);
    }
}
