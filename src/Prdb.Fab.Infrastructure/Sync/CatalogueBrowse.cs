using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// What the browse surfaces read. ADR 0012 makes five of them artwork grids,
/// and this is the first.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It reads the catalogue and nothing else.</strong> ADR 0027 has the
/// library grid never read the library; the counterpart here is that a browse
/// grid never reads prdb. The catalogue is what the sync routines keep current,
/// and a page of it is a query — so refreshing a grid costs no request, spends
/// no budget, and works with the network unplugged (ADR 0018).
/// </para>
/// <para>
/// <strong>No artwork is on the card.</strong> The grid asks
/// <c>/api/artwork/{videoId}</c> for a picture, which ADR 0030 answers from the
/// cache or fetches on sight — so nothing here has to know whether the bytes
/// are there, and a video whose image is missing costs this query nothing.
/// </para>
/// </remarks>
public sealed class CatalogueBrowse(FabDbContext context, FeedCursors cursors)
{
    /// <summary>
    /// How many videos a page of a grid holds.
    /// </summary>
    /// <remarks>
    /// Forty-eight, which divides by two, three, four and six — so every column
    /// count the grid falls into at a plausible window width ends on a full row
    /// rather than a ragged one.
    /// </remarks>
    public const int APage = 48;

    /// <summary>
    /// One page of What's New: the catalogue newest first, as prdb created it.
    /// </summary>
    /// <param name="page">
    /// Counted from one, because it is in the address bar (ADR 0036) and a
    /// person reads it there.
    /// </param>
    public async Task<VideoPage> WhatsNewAsync(int page, CancellationToken cancellationToken)
    {
        var wanted = Math.Max(page, 1);

        var total = await context.CatalogueVideos.CountAsync(cancellationToken);

        var videos = await context.CatalogueVideos
            // The order prdb publishes in, with the id breaking the tie so that
            // two requests for one page cannot answer with the same video in two
            // places (ADR 0036: a page has to be linkable, and a page whose
            // order is unstable is not).
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Skip((wanted - 1) * APage)
            .Take(APage)
            .Select(row => new VideoCard(
                row.Id,
                row.PrdbId,
                row.Title,
                row.Site == null ? null : row.Site.Title,
                row.ReleaseDate))
            .ToListAsync(cancellationToken);

        return new VideoPage(videos, wanted, APage, total);
    }

    /// <summary>
    /// One page of the wanted list: what the user has marked in prdb, most
    /// recently wanted first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read and never written. <c>CONTEXT.md</c> defines a Wanted Video as one
    /// the user has marked <em>in prdb</em>, and ADR 0007 makes that list the
    /// only source of intent — so this surface has no way to add to it and is
    /// not missing one.
    /// </para>
    /// <para>
    /// <strong>It reads the catalogue, not the feed's payload.</strong> ADR 0013
    /// observes that the feed carries enough to draw a card without a catalogue
    /// row; ADR 0033 then stores a wanted video as (video, since when) and makes
    /// it a pinning source. Read together, the payload is what <em>fills</em> a
    /// catalogue row that does not exist yet — which <see cref="WantedVideoFeed"/>
    /// already does — and the surface reads the row like every other surface.
    /// That keeps one card with one source, and it is what puts a wanted video
    /// under the repair pass and the artwork routine.
    /// </para>
    /// </remarks>
    public async Task<WantedList> WantedAsync(int page, CancellationToken cancellationToken)
    {
        var wanted = Math.Max(page, 1);

        var total = await context.WantedVideos.CountAsync(cancellationToken);

        var videos = await context.WantedVideos
            // Newest wanting first, which is prdb's own stamp rather than when a
            // feed read it — so a list restored onto a second installation comes
            // back in the order the user built it.
            .OrderByDescending(row => row.SinceAt)
            .ThenByDescending(row => row.VideoId)
            .Skip((wanted - 1) * APage)
            .Take(APage)
            .Select(row => new VideoCard(
                row.Video!.Id,
                row.Video.PrdbId,
                row.Video.Title,
                row.Video.Site == null ? null : row.Video.Site.Title,
                row.Video.ReleaseDate))
            .ToListAsync(cancellationToken);

        return new WantedList(
            videos,
            wanted,
            APage,
            total,
            await cursors.StartedAsync(Feed.WantedVideos, cancellationToken),
            await BackfillIsRunningAsync(cancellationToken));
    }

    /// <summary>
    /// Whether ADR 0013's backfill still has a row.
    /// </summary>
    /// <remarks>
    /// ADR 0014: bootstrap is not a state of the application, so there is no
    /// flag to read — a one-shot routine retires by deleting its row, and the
    /// row being there is the whole of what <em>still running</em> means. Asked
    /// of the schedule rather than of a column, which is what keeps the two from
    /// disagreeing.
    /// </remarks>
    private Task<bool> BackfillIsRunningAsync(CancellationToken cancellationToken) =>
        context.Routines.AnyAsync(
            row => row.Name == WhatsNewBackfillRoutine.RoutineName,
            cancellationToken);
}

/// <summary>
/// One card of a grid. ADR 0012: the five surfaces differ in their source and
/// their actions, never in the card.
/// </summary>
/// <param name="Id">
/// The catalogue's own id, which is what the artwork route is addressed by and
/// what a later slice will link a detail page on.
/// </param>
/// <param name="PrdbId">
/// prdb's own id, which is how anything outside this tool names the video —
/// and the only thing a link back to prdb can be built out of. ADR 0033 makes
/// that the natural key for exactly this reason.
/// </param>
/// <param name="Site">
/// The site's title, or null while the site list has not reached it. ADR 0013
/// replaces that list wholesale from a request of its own, so a video read
/// before it is a video whose site is not known here yet rather than one that
/// is wrong.
/// </param>
public sealed record VideoCard(
    long Id,
    Guid PrdbId,
    string Title,
    string? Site,
    DateOnly? ReleaseDate);

/// <summary>One page of a grid, and where in the whole it sits.</summary>
public sealed record VideoPage(IReadOnlyList<VideoCard> Videos, int Page, int PageSize, int Total);

/// <summary>
/// One page of the wanted list, and the two facts about the sync that decide
/// what an empty one says.
/// </summary>
/// <param name="FeedHasRun">
/// Whether the wanted list has ever been read from prdb. An empty list before
/// the first run and an empty list after it are different sentences: one is a
/// page that has not arrived, the other is an account with nothing on it.
/// </param>
/// <param name="BackfillRunning">
/// Whether ADR 0013's backfill still has a row to run. It is a fact and
/// explicitly not a Gap — nothing is broken, the catalogue is merely
/// unfinished — and it is the whole of what this slice says about the state of
/// the loop.
/// </param>
public sealed record WantedList(
    IReadOnlyList<VideoCard> Videos,
    int Page,
    int PageSize,
    int Total,
    bool FeedHasRun,
    bool BackfillRunning);
