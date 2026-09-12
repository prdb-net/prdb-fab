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
/// <strong>The second of the two triggers, and since ADR 0059 the smaller
/// one.</strong> <see cref="ArtworkRoutine"/> warms the pinned Videos and then
/// the front of the Catalogue behind them, so what reaches here is a tile the
/// warm pass has not got to yet — the tail of a long scroll, or a fresh
/// installation whose first fill is still running. The grid asks the tool and
/// never the CDN, and the tool serves the cached file or fetches, stores and
/// serves it. The <em>second</em> scroll is free, which is the property
/// <c>VISION.md</c> is buying; ADR 0059's finding was that the first one has to
/// be too.
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
    /// How stale <see cref="CatalogueImageRow.LastServedAt"/> is allowed to get
    /// before a serve rewrites it.
    /// </summary>
    /// <remarks>
    /// The stamp drives one thing — least-recently-served eviction — and an hour
    /// is finer than that needs. What the throttle buys is the price of a browse
    /// grid: two dozen tiles were two dozen writes against ADR 0004's single
    /// writer, every time the grid was drawn, contending on a spinning disk with
    /// the routines that write continuously. A second look at the same grid now
    /// writes nothing at all.
    /// </remarks>
    public static readonly TimeSpan ServedAgain = TimeSpan.FromHours(1);

    /// <summary>
    /// The bytes for a video, or why there are none, and the stamp that puts it
    /// at the back of the eviction queue.
    /// </summary>
    /// <remarks>
    /// Serving is what <see cref="CatalogueImageRow.LastServedAt"/> records —
    /// not fetching, and not the routine's fill. Eviction is
    /// least-recently-<em>served</em> first, so the stamp has to mean somebody
    /// looked at it.
    /// </remarks>
    public async Task<ArtworkAnswer> ServeAsync(long videoId, CancellationToken cancellationToken)
    {
        var image = await ChosenImages.OfAsync(context, videoId, cancellationToken);

        if (image is null || image.FoundDead)
        {
            // No image, or one marked dead and never asked about again
            // (ADR 0030). The caller draws the no-artwork tile.
            return ArtworkAnswer.Absent;
        }

        if (!image.Cached || !store.Holds(image.PrdbId))
        {
            var fetch = await FillAsync(image.PrdbId, image.Url, cancellationToken);

            await RecordAsync(image.Id, fetch, cancellationToken);

            if (fetch.Bytes is null)
            {
                return fetch.UrlIsDead ? ArtworkAnswer.Absent : ArtworkAnswer.NotNow;
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

            return ArtworkAnswer.NotNow;
        }

        var mediaType = await MediaTypeOfAsync(bytes, cancellationToken);

        if (mediaType is null)
        {
            // Bytes that are not an image, which the gateway refuses to store —
            // so this is a file somebody put there or one that was truncated.
            await bytes.DisposeAsync();

            return ArtworkAnswer.NotNow;
        }

        await ServedAsync(image, cancellationToken);

        return ArtworkAnswer.Of(new Served(bytes, mediaType));
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

    private Task ServedAsync(CatalogueImageRow image, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        if (image.LastServedAt is { } last && now - last < ServedAgain)
        {
            // Inside the window the stamp already says what it would be made to
            // say. The row is read either way; not writing it is the whole of
            // what this saves, and it is a write in the click path.
            return Task.CompletedTask;
        }

        return context.CatalogueImages
            .Where(row => row.Id == image.Id)
            .ExecuteUpdateAsync(
                row => row.SetProperty(cached => cached.LastServedAt, now),
                cancellationToken);
    }
}

/// <summary>
/// What the cache had for one video: the bytes, or an absence that either
/// stands or is only about this minute.
/// </summary>
/// <remarks>
/// The three ways of having nothing used to be one <see langword="null"/>, and
/// to a grid they still are — every one of them draws the no-artwork tile. They
/// differ to the <em>browser</em>: prdb publishing no image for a Video is a
/// property of the Catalogue and worth remembering for days, while a CDN that
/// did not answer in time is a property of this minute and worth remembering
/// for as long as it takes to try again. Collapsing the two means either
/// re-asking about thousands of Videos that will never have a picture, or
/// remembering one bad minute for a week.
/// </remarks>
/// <param name="Served">The bytes, or <see langword="null"/> where there are none.</param>
/// <param name="AbsenceStands">
/// Whether an absence is the Catalogue's answer rather than this attempt's.
/// False whenever <paramref name="Served"/> is not null, where it says nothing.
/// </param>
public sealed record ArtworkAnswer(Served? Served, bool AbsenceStands)
{
    /// <summary>prdb publishes no image for this Video, or none that still resolves.</summary>
    public static ArtworkAnswer Absent { get; } = new(null, true);

    /// <summary>A CDN that did not answer, or bytes that were not there after all.</summary>
    public static ArtworkAnswer NotNow { get; } = new(null, false);

    /// <summary>An image, on its way out.</summary>
    public static ArtworkAnswer Of(Served served) => new(served, false);
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
