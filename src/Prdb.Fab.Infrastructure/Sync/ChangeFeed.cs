using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// One of ADR 0013's five change feeds: what it is called at prdb, what it does
/// with a page, and where it belongs in ADR 0014's order of precedence.
/// </summary>
/// <remarks>
/// <para>
/// Split from the routine that turns it because two routines share one of these.
/// The actors feed is read every six hours by a recurring routine and, on a
/// fresh installation, as fast as the lane allows by the one-shot drain beside
/// it — the same request against the same cursor, at two cadences. Everything
/// that differs between those two is scheduling and none of it is the feed.
/// </para>
/// <para>
/// Reading and applying are one method rather than two. The generated payload
/// types differ per feed and stop here (ADR 0035), and a page that was read but
/// not applied is a page whose cursor has already moved past it.
/// </para>
/// </remarks>
public abstract class ChangeFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
{
    /// <summary>The row of <c>FeedCursor</c> this feed's position lives in.</summary>
    public abstract Feed Feed { get; }

    /// <summary>
    /// Whether a feed with no position starts at the beginning of what prdb
    /// holds, or at where prdb is now.
    /// </summary>
    /// <remarks>
    /// True for four of the five, and the whole of what a bootstrap is: an
    /// absent <c>since</c> is the documented way to page the current state of a
    /// feed from the start, which is how the wanted list and the favourites
    /// arrive and what the actors drain does for the actor corpus.
    /// </remarks>
    public virtual bool StartsAtTheBeginning => true;

    /// <summary>Where in ADR 0014's order this feed's requests are given up.</summary>
    /// <summary>
    /// Which of ADR 0014's kinds of work this feed's requests are. Public
    /// because the schedule asks: the idle profile is added up over it, and
    /// what is shed under a plan too small is expressed per kind of work rather
    /// than per routine (<see cref="IdleProfile"/>).
    /// </summary>
    public abstract PrdbWork Work { get; }

    protected FabDbContext Context { get; } = context;

    protected PrdbGateway Prdb { get; } = prdb;

    /// <summary>
    /// The catalogue rows a feed may have to create before its own row can
    /// exist. See <see cref="CatalogueRows"/>.
    /// </summary>
    protected CatalogueRows Catalogue { get; } = catalogue;

    /// <summary>
    /// Asks prdb for one page from <paramref name="from"/> and applies it.
    /// </summary>
    /// <param name="from">
    /// Where the feed stands, or null on its first ever run. What that means for
    /// the request is <see cref="FeedPosition.Since"/>'s to say.
    /// </param>
    public abstract Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition? from,
        int pageSize,
        CancellationToken cancellationToken);
}
