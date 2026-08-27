using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The first pass over prdb's actor corpus, and the one routine in this slice
/// that stops existing.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014: bootstrap is not a state of the application. An absent
/// <c>since</c> is the documented way to page the current state of a feed from
/// the beginning, and for the actors that is the whole corpus — far too much for
/// the six-hourly routine beside it, which would take months of pages to get
/// through it. So the drain is a routine of its own, running at the idle tick
/// from the first minute, and it retires when the feed says there is nothing
/// more.
/// </para>
/// <para>
/// <strong>It shares the recurring routine's cursor</strong> rather than keeping
/// a second one, because there is one walk over one feed and a second position
/// over it would be two answers to how far it has come. That is also why it is
/// in the sync lane and not the bulk one: two lanes are two workers, and two
/// workers writing one cursor is a race. ADR 0032's round-robin is what keeps it
/// from starving the feeds beside it — a run is one page, the lane takes a turn
/// elsewhere, and the governor holds it below the share ADR 0014 reserves for
/// actors.
/// </para>
/// <para>
/// <strong>It retires exactly once</strong>, because what says it has run is the
/// cursor row rather than the routine row. A routine with no row is given one
/// only where the feed has never written a position; after this one has been
/// through, the row is gone and there is a position, so no restart brings it
/// back. Between those two points the row is simply there, which is what makes a
/// restart mid-drain a resumption rather than a fresh start.
/// </para>
/// </remarks>
public sealed class ActorDrainRoutine(
    ActorFeed feed,
    FeedCursors cursors,
    FabDbContext context,
    IRoutineStore routines,
    ILogger<ActorDrainRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), IOneShot
{
    public const string RoutineName = "prdb.actors.drain";

    public override string Name => RoutineName;

    /// <summary>
    /// ADR 0032's idle tick for the sync lane. Not an interval: this routine has
    /// work until it has none, and what this says is how often to take the next
    /// turn rather than how often to act.
    /// </summary>
    public override TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<bool> StartsAsync(CancellationToken cancellationToken) =>
        !await Cursors.StartedAsync(Source.Feed, cancellationToken);

    protected override async Task ReachedAsync(FeedPosition position, CancellationToken cancellationToken)
    {
        if (position.Unfinished is not null)
        {
            return;
        }

        // Caught up. Everything from here is the six-hourly routine's, reading
        // on from the position this leaves behind.
        if (await routines.RetireAsync(Name, target: null, cancellationToken))
        {
            Logger.LogInformation("The actors drain has read prdb's actors to the end and retired.");
        }
    }
}
