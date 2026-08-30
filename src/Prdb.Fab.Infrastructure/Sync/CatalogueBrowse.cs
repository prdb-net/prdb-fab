using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Acquisition;
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
public sealed class CatalogueBrowse(
    FabDbContext context,
    FeedCursors cursors,
    ReleaseRankings rankings)
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
    public async Task<WhatsNewPage> WhatsNewAsync(int page, CancellationToken cancellationToken)
    {
        var wanted = Paging.Wanted(page);

        var total = await context.CatalogueVideos.CountAsync(cancellationToken);

        var videos = await context.CatalogueVideos
            // The order prdb publishes in, with the id breaking the tie so that
            // two requests for one page cannot answer with the same video in two
            // places (ADR 0036: a page has to be linkable, and a page whose
            // order is unstable is not).
            .OrderByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new VideoCard(
                row.Id,
                row.PrdbId,
                row.Title,
                row.Site == null ? null : row.Site.Title,
                row.Site == null ? null : row.Site.PrdbId,
                row.ReleaseDate))
            .ToListAsync(cancellationToken);

        var observed = await context.Installation
            .Select(row => new { row.WhatsNewObservedAt, row.WhatsNewObservedVideoId })
            .SingleAsync(cancellationToken);
        var newCount = observed.WhatsNewObservedAt is null
            ? total
            : await context.CatalogueVideos.CountAsync(row =>
                row.CreatedAtUtc > observed.WhatsNewObservedAt
                || (row.CreatedAtUtc == observed.WhatsNewObservedAt
                    && row.Id > observed.WhatsNewObservedVideoId), cancellationToken);
        var checkpoint = page == 1
            ? videos.FirstOrDefault()
            : null;

        return new WhatsNewPage(
            await WithAvailabilityAsync(videos, cancellationToken),
            wanted,
            APage,
            total,
            newCount,
            checkpoint?.Id,
            checkpoint is null
                ? null
                : await context.CatalogueVideos
                    .Where(row => row.Id == checkpoint.Id)
                    .Select(row => (DateTimeOffset?)row.CreatedAtUtc)
                    .SingleAsync(cancellationToken));
    }

    public async Task ObserveWhatsNewAsync(
        long videoId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var exists = await context.CatalogueVideos.AnyAsync(
            row => row.Id == videoId && row.CreatedAtUtc == createdAt,
            cancellationToken);
        if (!exists) return;

        var installation = await context.Installation.AsTracking().SingleAsync(cancellationToken);
        if (installation.WhatsNewObservedAt is null
            || createdAt > installation.WhatsNewObservedAt
            || (createdAt == installation.WhatsNewObservedAt
                && videoId > installation.WhatsNewObservedVideoId))
        {
            installation.WhatsNewObservedAt = createdAt;
            installation.WhatsNewObservedVideoId = videoId;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// One page of the wanted list: what the user has marked in prdb, most
    /// recently wanted first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Projected from the connected account, including ADR 0048's durable
    /// pending state while a manual acquisition converges its prdb write.
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
        var wanted = Paging.Wanted(page);

        var total = await context.WantedVideos.CountAsync(cancellationToken);

        var videos = await context.WantedVideos
            // Newest wanting first, which is prdb's own stamp rather than when a
            // feed read it — so a list restored onto a second installation comes
            // back in the order the user built it.
            .OrderByDescending(row => row.SinceAt)
            .ThenByDescending(row => row.VideoId)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new VideoCard(
                row.Video!.Id,
                row.Video.PrdbId,
                row.Video.Title,
                row.Video.Site == null ? null : row.Video.Site.Title,
                row.Video.Site == null ? null : row.Video.Site.PrdbId,
                row.Video.ReleaseDate))
            .ToListAsync(cancellationToken);

        return new WantedList(
            await WithAvailabilityAsync(videos, cancellationToken),
            wanted,
            APage,
            total,
            await cursors.StartedAsync(Feed.WantedVideos, cancellationToken),
            await RecentWindowIsFillingAsync(cancellationToken));
    }

    /// <summary>Searches every locally known Video without causing a remote read.</summary>
    public Task<VideoPage> VideosAsync(string? search, int page, CancellationToken cancellationToken) =>
        VideosAsync(context.CatalogueVideos.AsNoTracking(), search, page, cancellationToken);

    /// <summary>Sites kept by the catalogue, alphabetically and searched locally.</summary>
    public async Task<SitePage> SitesAsync(
        string? search,
        int page,
        CatalogueScope scope,
        CancellationToken cancellationToken)
    {
        var wanted = Paging.Wanted(page);
        var query = context.CatalogueSites.AsNoTracking();

        if (scope == CatalogueScope.Favourites)
        {
            query = query.Where(row => context.FavouriteSites.Any(favourite => favourite.SiteId == row.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row => EF.Functions.Like(
                row.Title, SearchPattern.Containing(search), SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var sites = await query
            .OrderByDescending(row => context.CatalogueVideos.Count(video => video.SiteId == row.Id))
            .ThenBy(row => row.Title)
            .ThenBy(row => row.Id)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new SiteCard(
                row.PrdbId,
                row.Title,
                row.Network,
                context.CatalogueVideos.Count(video => video.SiteId == row.Id),
                context.FavouriteSites.Any(favourite => favourite.SiteId == row.Id),
                context.CatalogueVideos
                    .Where(video => video.SiteId == row.Id)
                    .OrderByDescending(video => video.CreatedAtUtc)
                    .ThenBy(video => video.Id)
                    .Select(video => (long?)video.Id)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new SitePage(sites, wanted, APage, total, scope);
    }

    /// <summary>Actors kept by the catalogue, alphabetically and searched locally.</summary>
    public async Task<ActorPage> ActorsAsync(
        string? search,
        int page,
        CatalogueScope scope,
        CancellationToken cancellationToken)
    {
        var wanted = Paging.Wanted(page);
        var query = context.CatalogueActors.AsNoTracking();

        if (scope == CatalogueScope.Favourites)
        {
            query = query.Where(row => context.FavouriteActors.Any(favourite => favourite.ActorId == row.Id));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row => EF.Functions.Like(
                row.Name, SearchPattern.Containing(search), SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var actors = await query
            .OrderByDescending(row => context.CatalogueVideoActors.Count(credit => credit.ActorId == row.Id))
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new ActorCard(
                row.PrdbId,
                row.Name,
                context.CatalogueVideoActors.Count(credit => credit.ActorId == row.Id),
                context.FavouriteActors.Any(favourite => favourite.ActorId == row.Id),
                row.ProfileImageUrl != null))
            .ToListAsync(cancellationToken);

        return new ActorPage(actors, wanted, APage, total, scope);
    }

    /// <summary>One Site and the catalogue Videos released by it.</summary>
    public async Task<SiteVideos?> SiteAsync(
        Guid prdbId,
        string? search,
        int page,
        CancellationToken cancellationToken)
    {
        var site = await context.CatalogueSites
            .AsNoTracking()
            .Where(row => row.PrdbId == prdbId)
            .Select(row => new BrowseContext(
                row.PrdbId,
                row.Title,
                context.FavouriteSites.Any(favourite => favourite.SiteId == row.Id)))
            .SingleOrDefaultAsync(cancellationToken);

        if (site is null)
        {
            return null;
        }

        var videos = context.CatalogueVideos
            .AsNoTracking()
            .Where(row => row.Site != null && row.Site.PrdbId == prdbId);

        return new SiteVideos(site, await VideosAsync(videos, search, page, cancellationToken));
    }

    /// <summary>One Actor and the catalogue Videos carrying their credit.</summary>
    public async Task<ActorVideos?> ActorAsync(
        Guid prdbId,
        string? search,
        int page,
        CancellationToken cancellationToken)
    {
        var actor = await context.CatalogueActors
            .AsNoTracking()
            .Where(row => row.PrdbId == prdbId)
            .Select(row => new BrowseContext(
                row.PrdbId,
                row.Name,
                context.FavouriteActors.Any(favourite => favourite.ActorId == row.Id)))
            .SingleOrDefaultAsync(cancellationToken);

        if (actor is null)
        {
            return null;
        }

        var videos = context.CatalogueVideoActors
            .AsNoTracking()
            .Where(credit => credit.Actor != null && credit.Actor.PrdbId == prdbId)
            .Select(credit => credit.Video!);

        return new ActorVideos(actor, await VideosAsync(videos, search, page, cancellationToken));
    }

    /// <summary>
    /// Whether the first Recent Window fill is unfinished or a later pass is active.
    /// </summary>
    /// <remarks>
    /// The recurring routine never retires, so progress belongs to its durable
    /// source position rather than the existence of its schedule row.
    /// </remarks>
    private Task<bool> RecentWindowIsFillingAsync(CancellationToken cancellationToken) =>
        context.RecentWindowState.AnyAsync(
            row => row.CatalogueCompletedAt == null || row.CataloguePassStartedAt != null,
            cancellationToken);

    private async Task<VideoPage> VideosAsync(
        IQueryable<CatalogueVideoRow> query,
        string? search,
        int page,
        CancellationToken cancellationToken)
    {
        var wanted = Paging.Wanted(page);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row => EF.Functions.Like(
                row.Title, SearchPattern.Containing(search), SearchPattern.Escape));
        }

        var total = await query.CountAsync(cancellationToken);
        var videos = await query
            .OrderBy(row => row.Title)
            .ThenBy(row => row.Id)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .Select(row => new VideoCard(
                row.Id,
                row.PrdbId,
                row.Title,
                row.Site == null ? null : row.Site.Title,
                row.Site == null ? null : row.Site.PrdbId,
                row.ReleaseDate))
            .ToListAsync(cancellationToken);

        return new VideoPage(
            await WithAvailabilityAsync(videos, cancellationToken),
            wanted,
            APage,
            total);
    }

    private async Task<IReadOnlyList<VideoCard>> WithAvailabilityAsync(
        IReadOnlyList<VideoCard> videos,
        CancellationToken cancellationToken)
    {
        var ready = await rankings.ReadyVideosAsync(
            videos.Select(video => video.PrdbId).ToArray(),
            cancellationToken);
        var ids = videos.Select(video => video.PrdbId).ToArray();
        var localIds = videos.Select(video => video.Id).ToArray();
        var wanted = await context.WantedVideos
            .Where(row => localIds.Contains(row.VideoId))
            .Select(row => row.Video!.PrdbId)
            .ToListAsync(cancellationToken);
        var outstanding = await context.Downloads
            .Where(row => ids.Contains(row.VideoId) && row.State == DownloadState.Outstanding)
            .Select(row => row.VideoId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var qualityRows = await context.VideoFiles
            .Where(row => ids.Contains(row.LibraryEntryVideoId))
            .Select(row => new { VideoId = row.LibraryEntryVideoId, row.QualityLabel })
            .Distinct()
            .ToListAsync(cancellationToken);
        var qualities = qualityRows
            .GroupBy(row => row.VideoId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.Select(row => row.QualityLabel).Order()]);
        var matched = await context.Releases
            .Where(row => row.VideoId.HasValue
                && localIds.Contains(row.VideoId.Value)
                && row.IdentificationState == IdentificationState.Matched)
            .Select(row => row.VideoId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var wantedWrites = await context.AccountPreferenceWrites
            .Where(row => row.Kind == AccountPreferenceKind.WantedVideo && ids.Contains(row.EntityId))
            .Select(row => new { row.EntityId, row.Blocked, row.LastFailure })
            .ToDictionaryAsync(row => row.EntityId, cancellationToken);

        var wantedSet = wanted.ToHashSet();
        var outstandingSet = outstanding.ToHashSet();
        var matchedSet = matched.ToHashSet();
        return [.. videos.Select(video => video with
        {
            DownloadReady = ready.Contains(video.PrdbId),
            Wanted = wantedSet.Contains(video.PrdbId),
            WantedSyncPending = wantedWrites.TryGetValue(video.PrdbId, out var write) && !write.Blocked,
            WantedSyncFailure = wantedWrites.GetValueOrDefault(video.PrdbId)?.LastFailure,
            Outstanding = outstandingSet.Contains(video.PrdbId),
            HeldQualities = qualities.GetValueOrDefault(video.PrdbId, []),
            Availability = ready.Contains(video.PrdbId)
                ? VideoAvailability.Ready
                : matchedSet.Contains(video.Id)
                    ? VideoAvailability.ReleasesNeedInspection
                    : VideoAvailability.NoIdentifiedRelease,
        })];
    }
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
/// <param name="DownloadReady">
/// Whether the Video has a ranked, unconsumed Release and retry budget for one
/// more submission. Computed by the acquisition ranking rather than inferred
/// from the presence of a cached Release.
/// </param>
public sealed record VideoCard(
    long Id,
    Guid PrdbId,
    string Title,
    string? Site,
    Guid? SitePrdbId,
    DateOnly? ReleaseDate,
    bool DownloadReady = false,
    bool Wanted = false,
    bool WantedSyncPending = false,
    string? WantedSyncFailure = null,
    bool Outstanding = false,
    IReadOnlyList<string>? HeldQualities = null,
    VideoAvailability Availability = VideoAvailability.NoIdentifiedRelease);

public enum VideoAvailability
{
    Ready,
    ReleasesNeedInspection,
    NoIdentifiedRelease,
}

/// <summary>One page of a grid, and where in the whole it sits.</summary>
public sealed record VideoPage(IReadOnlyList<VideoCard> Videos, int Page, int PageSize, int Total);

public sealed record WhatsNewPage(
    IReadOnlyList<VideoCard> Videos,
    int Page,
    int PageSize,
    int Total,
    int NewCount,
    long? CheckpointVideoId,
    DateTimeOffset? CheckpointCreatedAt);

/// <summary>
/// One page of the wanted list, and the two facts about the sync that decide
/// what an empty one says.
/// </summary>
/// <param name="FeedHasRun">
/// Whether the wanted list has ever been read from prdb. An empty list before
/// the first run and an empty list after it are different sentences: one is a
/// page that has not arrived, the other is an account with nothing on it.
/// </param>
/// <param name="RecentWindowFilling">
/// Whether the first Recent Window Catalogue proof is incomplete or another
/// complete pass is in progress.
/// </param>
public sealed record WantedList(
    IReadOnlyList<VideoCard> Videos,
    int Page,
    int PageSize,
    int Total,
    bool FeedHasRun,
    bool RecentWindowFilling);

public sealed record BrowseContext(Guid PrdbId, string Title, bool Favourite);

public sealed record SiteCard(
    Guid PrdbId,
    string Title,
    string? Network,
    int VideoCount,
    bool Favourite,
    long? RepresentativeVideoId);

public sealed record ActorCard(Guid PrdbId, string Name, int VideoCount, bool Favourite, bool HasArtwork);

public sealed record SitePage(
    IReadOnlyList<SiteCard> Sites,
    int Page,
    int PageSize,
    int Total,
    CatalogueScope Scope);

public sealed record ActorPage(
    IReadOnlyList<ActorCard> Actors,
    int Page,
    int PageSize,
    int Total,
    CatalogueScope Scope);

public enum CatalogueScope
{
    Favourites,
    All,
}

public sealed record SiteVideos(BrowseContext Site, VideoPage Videos);

public sealed record ActorVideos(BrowseContext Actor, VideoPage Videos);
