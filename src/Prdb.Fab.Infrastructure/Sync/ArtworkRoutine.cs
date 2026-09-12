using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0030's sixth routine: fetch the artwork of pinned videos, warm the rest
/// of the Catalogue behind them, and hold both caches to their ceilings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The first of the two triggers.</strong> The library grid shows one
/// image per held video and ADR 0027 requires a held video's image to be on
/// disk so that filing has something to copy — and neither tolerates
/// <em>fetch it when someone looks</em>: the first would show a grid of blanks
/// on a fresh restore, the second would put a network read inside the file
/// lane, which ADR 0026 built to wait on nothing.
/// </para>
/// <para>
/// <strong>And the warm pass behind it.</strong> ADR 0059 reversed ADR 0030's
/// <em>everything unpinned is fetched when a grid asks</em>: at 1.2 % cached, a
/// grid of two dozen tiles meant two dozen live CDN fetches before it was
/// complete, and the click took seconds while the queries behind it took
/// milliseconds. So the unpinned Catalogue is warmed too — newest release
/// first, because that is the order What's New and Catalogue Search's default
/// come back in — until the unpinned half reaches
/// <see cref="ArtworkCeiling.WarmTo"/>. Pinned work goes first and is never
/// waited on by it: the two are separate passes of the same turn.
/// </para>
/// <para>
/// <strong>Newly pinned first.</strong> <see cref="CataloguePins.NewestPinFirst"/>
/// is what puts a freshly downloaded video's image on disk within a minute or
/// two of the catalogue row being pinned — comfortably inside the hours a
/// cross-filesystem copy takes. A consequence rather than a promise: ADR 0027
/// already fixed what happens if the image is not there, and it is nothing.
/// </para>
/// <para>
/// <strong>It spends no prdb budget, so it asks the governor nothing.</strong>
/// An image URL is a <c>GET</c> against a CDN carrying no key
/// (<see cref="ArtworkGateway"/>). What stands in the governor's place is a
/// small fixed concurrency here, a short timeout and a size ceiling there. This
/// is also why ADR 0030 refused to fold the work into ADR 0013's repair pass:
/// that pass is steered by a scarce request budget, and attaching a free local
/// job to it would make artwork arrive at the speed of the rate limit for no
/// reason.
/// </para>
/// <para>
/// <strong>Both evictions are here.</strong> ADR 0030 puts the artwork sweep in
/// the same routine as its own work set, and ADR 0033 left
/// <see cref="CatalogueEviction"/> without a routine of its own for exactly
/// this. Running them in this order is what lets one pass clean up after the
/// other: a catalogue row dropped here takes its image rows with it by cascade,
/// and the files they leave are swept in the same tick rather than the next.
/// </para>
/// </remarks>
public sealed class ArtworkRoutine(
    FabDbContext context,
    CataloguePins pins,
    ArtworkCache cache,
    ActorArtworkCache actorCache,
    CatalogueEviction catalogue,
    ArtworkEviction artwork,
    ILogger<ArtworkRoutine> logger) : IRoutine
{
    public const string RoutineName = "prdb.artwork";

    /// <summary>
    /// How many images one pass fetches.
    /// </summary>
    /// <remarks>
    /// A bounded run yields, which is the shape every routine in this slice
    /// has: being behind is answered by coming round again rather than by not
    /// stopping. ADR 0032 makes this routine due again immediately while its
    /// work set is not empty, so a restored installation's backlog is taken a
    /// hundred at a time without the bulk lane being held for the whole of it.
    /// </remarks>
    public const int AWindow = 100;

    /// <summary>
    /// How many images are fetched at once.
    /// </summary>
    /// <remarks>
    /// ADR 0030's small fixed concurrency, and the reason it is small: a
    /// backfill of a few thousand images must not saturate the line the
    /// downloader is on. Four, which keeps a pass short against the latency of
    /// a CDN without being a burst anybody would notice.
    /// </remarks>
    public const int AtOnce = 4;

    /// <summary>
    /// How far down the Actors grid the warm pass reaches.
    /// </summary>
    /// <remarks>
    /// Unlike the Videos, the Actors cannot simply be warmed until the budget
    /// is full: a Catalogue holding ~47 000 Videos holds ~323 000 Actors, which
    /// at the size a profile image runs to is several times the whole ceiling.
    /// So this is a front rather than a set — the two thousand the grid puts on
    /// its first eighty pages, which is further than anybody scrolls a list
    /// ordered by credit count. Everything behind it is served lazily, exactly
    /// as every Actor was before.
    /// </remarks>
    public const int AnActorFront = 2_000;

    public string Name => RoutineName;

    /// <summary>ADR 0030 puts this in the bulk lane, beside the repair pass.</summary>
    public Lane Lane => Lane.Bulk;

    /// <summary>
    /// ADR 0032's idle tick for the bulk lane. Not an interval: the work set is
    /// a query over a state, so this says how often to take the next turn rather
    /// than how often there is anything to do.
    /// </summary>
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var fetched = await FillAsync(cancellationToken);

        // After the fetch, so that a row dropped here leaves its files to be
        // swept in the same pass rather than the next one.
        var evicted = await catalogue.EvictAsync(cancellationToken: cancellationToken);
        var swept = await artwork.SweepAsync(cancellationToken: cancellationToken);

        // Last, and on the figure the sweep has just read off the disk: warming
        // has to know how much room is left, and the walk that answers that has
        // already happened this turn. It is the pre-eviction weight, which is
        // the conservative one — the half is never heavier than the sweep found
        // it.
        var warmed = await WarmAsync(
            swept.UnpinnedBytes,
            cancellationToken: cancellationToken);

        if (fetched == 0 && warmed == 0 && evicted.Removed == 0 && !swept.DidSomething)
        {
            // ADR 0032: an empty work set is not a run. Nothing was fetched,
            // nothing was over a ceiling, and nothing was left behind — so this
            // is not recorded and moves no counter.
            return RunResult.NothingToDo;
        }

        return RunResult.Handled(fetched + warmed + evicted.Removed + swept.Evicted + swept.Orphans);
    }

    /// <summary>
    /// One window of the unpinned Catalogue, newest release first, and one of
    /// the front of the Actors grid — both only while there is room under
    /// <see cref="ArtworkCeiling.WarmTo"/>.
    /// </summary>
    /// <remarks>
    /// The budget is checked once for the turn rather than per image. A window
    /// is a hundred images of a few hundred kilobytes, and the gap between
    /// <see cref="ArtworkCeiling.WarmTo"/> and <see cref="ArtworkCeiling.Bytes"/>
    /// is two gigabytes, so the overshoot a coarse check allows is two orders of
    /// magnitude inside it.
    /// </remarks>
    /// <param name="held">
    /// What the unpinned half weighs, which the caller has just read off the
    /// disk.
    /// </param>
    /// <param name="warmTo">
    /// Where to stop, defaulted the way <see cref="ArtworkEviction.SweepAsync"/>
    /// defaults its ceiling — so that a test can move the bound without the
    /// bound being a setting.
    /// </param>
    public async Task<int> WarmAsync(
        long held,
        long warmTo = ArtworkCeiling.WarmTo,
        CancellationToken cancellationToken = default)
    {
        if (held >= warmTo)
        {
            return 0;
        }

        return await WarmVideosAsync(cancellationToken)
            + await WarmActorsAsync(cancellationToken);
    }

    /// <summary>
    /// The unpinned Videos whose chosen image is not in the cache, newest
    /// release first.
    /// </summary>
    /// <remarks>
    /// Driven from the images rather than from the Videos, for
    /// <see cref="FillAsync"/>'s reason: the filtered index on <c>cached</c>
    /// narrows to what is actually missing before pinning is asked about, and
    /// once the warm pass has caught up that set is empty rather than enormous.
    /// Videos with no release date sort last — they are the ones no browse
    /// surface puts on a first page either.
    /// </remarks>
    private async Task<int> WarmVideosAsync(CancellationToken cancellationToken)
    {
        var pending = ChosenImages.In(
            context,
            context.CatalogueImages.Where(image => !image.Cached && !image.FoundDead));

        var due = await pending
            .Join(
                pins.Unpinned(context.CatalogueVideos),
                image => image.VideoId,
                video => video.Id,
                (image, video) => new { Image = image, video.ReleaseDate })
            .OrderByDescending(row => row.ReleaseDate.HasValue)
            .ThenByDescending(row => row.ReleaseDate)
            .ThenByDescending(row => row.Image.VideoId)
            .Take(AWindow)
            .Select(row => new Due(row.Image.Id, row.Image.VideoId, row.Image.PrdbId, row.Image.Url))
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        var arrived = 0;

        foreach (var (image, fetch) in await FetchAsync(due, cancellationToken))
        {
            await cache.RecordAsync(image.Id, fetch, cancellationToken);

            if (fetch.Bytes is not null)
            {
                arrived++;
            }
        }

        logger.LogInformation(
            "The artwork routine warmed {Arrived} image(s) of the {Asked} unpinned Video(s) it asked for.",
            arrived,
            due.Count);

        return arrived;
    }

    /// <summary>
    /// The uncached Actors inside <see cref="AnActorFront"/>, in the order the
    /// Actors grid puts them in.
    /// </summary>
    private async Task<int> WarmActorsAsync(CancellationToken cancellationToken)
    {
        var front = context.CatalogueActors
            .Where(row => row.ProfileImageUrl != null && row.ArtworkCacheKey != null)
            .OrderByDescending(row => context.CatalogueVideoActors.Count(credit => credit.ActorId == row.Id))
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Take(AnActorFront);

        var due = await front
            .Where(row => !row.ArtworkCached && !row.ArtworkFoundDead)
            .Take(AWindow)
            .Select(row => new DueActor(row.Id, row.ArtworkCacheKey!.Value, row.ProfileImageUrl!))
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        var fetches = new List<(DueActor Actor, ArtworkFetch Fetch)>(due.Count);

        foreach (var batch in due.Chunk(AtOnce))
        {
            var running = batch
                .Select(async actor =>
                    (actor, await cache.FillAsync(actor.CacheKey, actor.Url, cancellationToken)))
                .ToList();

            fetches.AddRange(await Task.WhenAll(running));
        }

        var arrived = 0;

        foreach (var (actor, fetch) in fetches)
        {
            await actorCache.RecordAsync(actor.Id, fetch, cancellationToken);

            if (fetch.Bytes is not null)
            {
                arrived++;
            }
        }

        logger.LogInformation(
            "The artwork routine warmed {Arrived} profile image(s) of the {Asked} Actor(s) it asked for.",
            arrived,
            due.Count);

        return arrived;
    }

    /// <summary>
    /// One window of pinned videos whose chosen image is not in the cache.
    /// </summary>
    private async Task<int> FillAsync(CancellationToken cancellationToken)
    {
        // From the images rather than from the videos, and the filtered index on
        // `cached` is why: an installed cache has almost nothing uncached in it,
        // so this narrows to a handful of rows before pinning is asked about at
        // all. A video whose image is marked dead is not here — ADR 0030 marks
        // once and never asks again.
        var pending = ChosenImages.In(
            context,
            context.CatalogueImages.Where(image => !image.Cached && !image.FoundDead));

        var due = await pins.NewestPinFirst(context.CatalogueVideos)
            .Where(video => pending.Any(image => image.VideoId == video.Id))
            .Take(AWindow)
            .Select(video => video.Id)
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        var found = await pending
            .Where(image => due.Contains(image.VideoId))
            .Select(image => new Due(image.Id, image.VideoId, image.PrdbId, image.Url))
            .ToListAsync(cancellationToken);

        // Back into the order the videos came in. The query above answers about
        // a set and says nothing about sequence, so newly-pinned-first would be
        // lost here — which is the half of ADR 0030's promise that a second
        // query cannot restate.
        var order = due
            .Select((videoId, place) => (videoId, place))
            .ToDictionary(entry => entry.videoId, entry => entry.place);

        var images = found.OrderBy(image => order[image.VideoId]).ToList();

        var fetches = await FetchAsync(images, cancellationToken);

        var arrived = 0;

        // The rows afterwards and one at a time: the network above ran in
        // parallel because the gateway and the store hold no state, and the
        // connection under this context is one thing that may not be used from
        // two places at once.
        foreach (var (image, fetch) in fetches)
        {
            await cache.RecordAsync(image.Id, fetch, cancellationToken);

            if (fetch.Bytes is not null)
            {
                arrived++;
            }
        }

        logger.LogInformation(
            "The artwork routine cached {Arrived} image(s) of the {Asked} pinned video(s) it asked for.",
            arrived,
            images.Count);

        return arrived;
    }

    /// <summary>
    /// The window, <see cref="AtOnce"/> at a time.
    /// </summary>
    /// <remarks>
    /// Nothing is caught here. A failure is a verdict rather than an exception
    /// (<see cref="ArtworkFetch"/>), and a cancellation is the lane's to read as
    /// an interruption — every image is written independently, so a pass cut
    /// short leaves the ones it finished cached and the rest exactly as they
    /// were.
    /// </remarks>
    private async Task<IReadOnlyList<(Due Image, ArtworkFetch Fetch)>> FetchAsync(
        IReadOnlyList<Due> images,
        CancellationToken cancellationToken)
    {
        var fetches = new List<(Due, ArtworkFetch)>(images.Count);

        foreach (var batch in images.Chunk(AtOnce))
        {
            var running = batch
                .Select(async image =>
                    (image, await cache.FillAsync(image.PrdbId, image.Url, cancellationToken)))
                .ToList();

            fetches.AddRange(await Task.WhenAll(running));
        }

        return fetches;
    }

    /// <summary>An image this pass is about to fetch, by both of its names.</summary>
    private sealed record Due(long Id, long VideoId, Guid PrdbId, string Url);

    /// <summary>An Actor profile image this pass is about to fetch.</summary>
    private sealed record DueActor(long Id, Guid CacheKey, string Url);
}
