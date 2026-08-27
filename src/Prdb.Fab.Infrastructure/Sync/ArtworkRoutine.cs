using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0030's sixth routine: fetch the artwork of pinned videos, and hold both
/// caches to their ceilings.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The first of the two triggers.</strong> The library grid shows one
/// image per held video and ADR 0027 requires a held video's image to be on
/// disk so that filing has something to copy — and neither tolerates
/// <em>fetch it when someone looks</em>: the first would show a grid of blanks
/// on a fresh restore, the second would put a network read inside the file
/// lane, which ADR 0026 built to wait on nothing. Everything unpinned is
/// <see cref="ArtworkCache"/>'s, fetched when a grid asks.
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

        if (fetched == 0 && evicted.Removed == 0 && !swept.DidSomething)
        {
            // ADR 0032: an empty work set is not a run. Nothing was fetched,
            // nothing was over a ceiling, and nothing was left behind — so this
            // is not recorded and moves no counter.
            return RunResult.NothingToDo;
        }

        return RunResult.Handled(fetched + evicted.Removed + swept.Evicted + swept.Orphans);
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
}
