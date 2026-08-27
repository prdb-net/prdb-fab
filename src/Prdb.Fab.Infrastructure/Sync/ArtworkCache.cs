using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0030's cache, from the outside: the bytes for one video, fetched if they
/// are not there yet.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The second of the two triggers.</strong> Pinned videos are filled by
/// <see cref="ArtworkRoutine"/> ahead of anybody looking; everything else is
/// fetched when a grid asks, because What's New, Sites, Actors and Wanted range
/// over a catalogue nobody scrolls all of. The grid asks the tool and never the
/// CDN, and the tool serves the cached file or fetches, stores and serves it.
/// The <em>second</em> scroll is free, which is the property <c>VISION.md</c> is
/// buying.
/// </para>
/// <para>
/// <strong>A page request may do network I/O here</strong>, which is the first
/// place in this tool that is true. What keeps it from being the first place a
/// page request can hang is the transport's short timeout
/// (<see cref="FabTransports.ArtworkTimeout"/>) and a caller that draws the
/// no-artwork tile rather than waiting. It spends no prdb budget, so ADR 0018's
/// rule that refreshing never causes work is intact: what that rule protects is
/// the governor and the indexers' daily budgets, and neither is touched.
/// </para>
/// </remarks>
public sealed class ArtworkCache(
    FabDbContext context,
    ArtworkStore store,
    ArtworkGateway gateway,
    TimeProvider time,
    ILogger<ArtworkCache> logger)
{
    /// <summary>
    /// The bytes for a video, and the stamp that puts it at the back of the
    /// eviction queue.
    /// </summary>
    /// <remarks>
    /// Serving is what <see cref="CatalogueImageRow.LastServedAt"/> records —
    /// not fetching, and not the routine's fill. Eviction is
    /// least-recently-<em>served</em> first, so the stamp has to mean somebody
    /// looked at it.
    /// </remarks>
    public async Task<Served?> ServeAsync(long videoId, CancellationToken cancellationToken)
    {
        var image = await ChosenImages.OfAsync(context, videoId, cancellationToken);

        if (image is null || image.FoundDead)
        {
            // No image, or one marked dead and never asked about again
            // (ADR 0030). The caller draws the no-artwork tile.
            return null;
        }

        if (!image.Cached || !store.Holds(image.PrdbId))
        {
            var fetch = await FillAsync(image.PrdbId, image.Url, cancellationToken);

            await RecordAsync(image.Id, fetch, cancellationToken);

            if (fetch.Bytes is null)
            {
                return null;
            }
        }

        var bytes = store.Open(image.PrdbId);

        if (bytes is null)
        {
            // The file went between the check above and here — an eviction pass,
            // or somebody with a shell. Nothing is wrong that the next request
            // will not fix, and the mark is corrected on the way past.
            await context.CatalogueImages
                .Where(row => row.Id == image.Id)
                .ExecuteUpdateAsync(
                    row => row.SetProperty(cached => cached.Cached, false),
                    cancellationToken);

            return null;
        }

        var mediaType = await MediaTypeOfAsync(bytes, cancellationToken);

        if (mediaType is null)
        {
            // Bytes that are not an image, which the gateway refuses to store —
            // so this is a file somebody put there or one that was truncated.
            await bytes.DisposeAsync();

            return null;
        }

        await ServedAsync(image.Id, cancellationToken);

        return new Served(bytes, mediaType);
    }

    /// <summary>
    /// What the file on disk is, from its own first bytes, leaving the stream
    /// where it found it.
    /// </summary>
    private static async Task<string?> MediaTypeOfAsync(
        Stream bytes,
        CancellationToken cancellationToken)
    {
        var header = new byte[ArtworkFormat.Header];

        var read = await bytes.ReadAtLeastAsync(
            header,
            header.Length,
            throwOnEndOfStream: false,
            cancellationToken);

        bytes.Position = 0;

        return ArtworkFormat.MediaTypeOf(header.AsSpan(0, read));
    }

    /// <summary>
    /// Fetches one image and puts it in the cache, touching no row.
    /// </summary>
    /// <remarks>
    /// Separated from the write on purpose, and it is what lets
    /// <see cref="ArtworkRoutine"/> run several of these at once: the gateway
    /// and the store hold no per-call state, while the <c>DbContext</c> above
    /// them is one connection that may not be used from two places at a time.
    /// So the network happens in parallel and the rows are written afterwards,
    /// in one pass, by whoever asked.
    /// </remarks>
    public async Task<ArtworkFetch> FillAsync(
        Guid imageId,
        string url,
        CancellationToken cancellationToken)
    {
        var fetch = await gateway.FetchAsync(url, cancellationToken);

        if (fetch.Bytes is not null)
        {
            await store.WriteAsync(imageId, fetch.Bytes, cancellationToken);
        }
        else if (fetch.UrlIsDead)
        {
            // The bytes under this id, if any, are what prdb no longer
            // publishes. Nothing points at them any more.
            store.Delete(imageId);
        }

        return fetch;
    }

    /// <summary>
    /// Writes what a fetch established: cached, dead, or neither.
    /// </summary>
    /// <remarks>
    /// A transport failure writes nothing at all. ADR 0030 is explicit that it
    /// is not a dead URL — the same distinction ADR 0016 draws between a request
    /// that failed and an id that was genuinely absent — and collapsing the two
    /// would turn one flaky minute into a grid of permanent blanks.
    /// </remarks>
    public async Task RecordAsync(long imageId, ArtworkFetch fetch, CancellationToken cancellationToken)
    {
        if (fetch.Bytes is not null)
        {
            await context.CatalogueImages
                .Where(row => row.Id == imageId)
                .ExecuteUpdateAsync(
                    row => row.SetProperty(image => image.Cached, true),
                    cancellationToken);

            return;
        }

        if (!fetch.UrlIsDead)
        {
            return;
        }

        logger.LogInformation("An image URL was found dead and will not be fetched again.");

        await context.CatalogueImages
            .Where(row => row.Id == imageId)
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(image => image.Cached, false)
                    .SetProperty(image => image.FoundDead, true),
                cancellationToken);
    }

    private Task ServedAsync(long imageId, CancellationToken cancellationToken) =>
        context.CatalogueImages
            .Where(row => row.Id == imageId)
            .ExecuteUpdateAsync(
                row => row.SetProperty(image => image.LastServedAt, time.GetUtcNow()),
                cancellationToken);
}

/// <summary>
/// One image on its way to a browser: the bytes, and what they are.
/// </summary>
/// <remarks>
/// The media type is read off the file rather than kept beside the row, which
/// is <see cref="ArtworkFormat"/>'s reason for existing — the answer is in the
/// bytes, and a column holding it would be a second place for it to be wrong.
/// </remarks>
public sealed record Served(Stream Bytes, string MediaType);
