using Microsoft.EntityFrameworkCore;

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
public sealed class CatalogueBrowse(FabDbContext context)
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
                row.Title,
                row.Site == null ? null : row.Site.Title,
                row.ReleaseDate))
            .ToListAsync(cancellationToken);

        return new VideoPage(videos, wanted, APage, total);
    }
}

/// <summary>
/// One card of a grid. ADR 0012: the five surfaces differ in their source and
/// their actions, never in the card.
/// </summary>
/// <param name="Id">
/// The catalogue's own id, which is what the artwork route is addressed by and
/// what a later slice will link a detail page on. Not prdb's id: nothing
/// outside the tool is being named here.
/// </param>
/// <param name="Site">
/// The site's title, or null while the site list has not reached it. ADR 0013
/// replaces that list wholesale from a request of its own, so a video read
/// before it is a video whose site is not known here yet rather than one that
/// is wrong.
/// </param>
public sealed record VideoCard(long Id, string Title, string? Site, DateOnly? ReleaseDate);

/// <summary>One page of a grid, and where in the whole it sits.</summary>
public sealed record VideoPage(IReadOnlyList<VideoCard> Videos, int Page, int PageSize, int Total);
