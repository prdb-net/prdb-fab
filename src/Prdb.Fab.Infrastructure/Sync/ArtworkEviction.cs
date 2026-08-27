using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Holding the artwork cache to <see cref="ArtworkCeiling"/>: the unpinned half
/// evicted least-recently-served first, and the files nothing names at all
/// swept up.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The ceiling is over the unpinned half only.</strong> Pinned images
/// are the library grid and the file filing copies from, so evicting one would
/// mean a held video with no picture and a routine fetching it straight back.
/// They are not counted against the ceiling either — what the user has is
/// theirs to have, and the bound is on the half <c>VISION.md</c> calls
/// disposable.
/// </para>
/// <para>
/// <strong>The disk is the authority on how much disk is used.</strong> Sizes
/// are read off the files rather than kept in a column, for the reason ADR 0033
/// gave about the pin: a stored size would have one writer, no reader that
/// would notice it drifting, and a symptom — a cache quietly over its ceiling —
/// that nothing would connect back to it. The walk that reads them is 256
/// directories deep by ADR 0030's fan-out and happens once per pass of a
/// routine that runs at the bulk lane's idle tick.
/// </para>
/// <para>
/// <strong>Unpinning deletes nothing.</strong> It makes a file evictable, and
/// the next pass may or may not take it — which is what ADR 0030 means by the
/// unpinned half being bounded rather than swept.
/// </para>
/// <para>
/// Unlike the indexer cache there is no Gap when the ceiling cannot be held.
/// ADR 0015 refuses to drop a release nobody has looked at because a dropped
/// release is a wanted video never found; here the worst case is a picture
/// fetched twice.
/// </para>
/// </remarks>
public sealed class ArtworkEviction(
    FabDbContext context,
    CataloguePins pins,
    ArtworkStore store,
    ILogger<ArtworkEviction> logger)
{
    /// <summary>
    /// How many files one question about the database covers.
    /// </summary>
    /// <remarks>
    /// The cache is thousands of files and the catalogue's image table is
    /// larger still, so neither is loaded whole to compare against the other.
    /// Asking in batches keeps both the query and the memory bounded, and the
    /// answer is the same one a single enormous <c>IN</c> would give.
    /// </remarks>
    public const int ABatch = 500;

    /// <summary>
    /// Takes the unpinned half back under <paramref name="ceiling"/> and drops
    /// whatever the catalogue no longer names.
    /// </summary>
    public async Task<ArtworkSweep> SweepAsync(
        long ceiling = ArtworkCeiling.Bytes,
        CancellationToken cancellationToken = default)
    {
        var onDisk = Sizes();

        if (onDisk.Count == 0)
        {
            return ArtworkSweep.Nothing;
        }

        var evictable = new List<Cached>();
        var orphans = new List<Guid>();

        foreach (var batch in onDisk.Keys.Chunk(ABatch))
        {
            await ExamineAsync(batch, onDisk, evictable, orphans, cancellationToken);
        }

        // By subtraction, because every file in the cache is exactly one of the
        // three: pinned, evictable, or named by nothing.
        var pinned = onDisk.Count - evictable.Count - orphans.Count;

        foreach (var orphan in orphans)
        {
            // A catalogue row was evicted and took its image rows with it by
            // cascade (ADR 0033), or prdb hard-deleted the image and a detail
            // read noticed. Either way the bytes are named by an id nothing
            // points at.
            store.Delete(orphan);
        }

        var held = evictable.Sum(image => image.Bytes);
        var over = ArtworkCeiling.OverBy(held, ceiling);

        var removed = over == 0
            ? 0
            : await EvictAsync(evictable, over, cancellationToken);

        if (removed > 0 || orphans.Count > 0)
        {
            logger.LogInformation(
                "The artwork cache dropped {Removed} unpinned image(s) and {Orphans} nothing named, "
                + "leaving {Held} byte(s) of an unpinned {Ceiling}.",
                removed,
                orphans.Count,
                held,
                ceiling);
        }

        return new ArtworkSweep(held, pinned, removed, orphans.Count);
    }

    /// <summary>
    /// Every file in the cache and what it weighs.
    /// </summary>
    private Dictionary<Guid, long> Sizes()
    {
        var sizes = new Dictionary<Guid, long>();

        foreach (var imageId in store.Held())
        {
            if (store.SizeOf(imageId) is { } bytes)
            {
                sizes[imageId] = bytes;
            }
        }

        return sizes;
    }

    /// <summary>
    /// Sorts one batch of files into the pinned, the evictable and the
    /// unnamed.
    /// </summary>
    private async Task ExamineAsync(
        Guid[] batch,
        Dictionary<Guid, long> onDisk,
        List<Cached> evictable,
        List<Guid> orphans,
        CancellationToken cancellationToken)
    {
        var known = await context.CatalogueImages
            .Where(row => batch.Contains(row.PrdbId))
            .Select(row => new { row.Id, row.PrdbId, row.LastServedAt })
            .ToListAsync(cancellationToken);

        // The pinned ones of this batch, by the query ADR 0033 made pinning
        // into. Joined from the video side because that is the side the clauses
        // are written about.
        var isPinned = await pins.Pinned(context.CatalogueVideos)
            .Join(
                context.CatalogueImages.Where(row => batch.Contains(row.PrdbId)),
                video => video.Id,
                image => image.VideoId,
                (_, image) => image.PrdbId)
            .ToListAsync(cancellationToken);

        var named = known.Select(row => row.PrdbId).ToHashSet();

        orphans.AddRange(batch.Where(imageId => !named.Contains(imageId)));

        evictable.AddRange(known
            .Where(row => !isPinned.Contains(row.PrdbId))
            .Select(row => new Cached(row.Id, row.PrdbId, row.LastServedAt, onDisk[row.PrdbId])));
    }

    /// <summary>
    /// Drops least-recently-served images until <paramref name="over"/> bytes
    /// have gone.
    /// </summary>
    /// <remarks>
    /// A file never served sorts first: it is in the cache because a fetch put
    /// it there, and nothing has asked for it since. The row stays and only
    /// loses its <c>cached</c> mark, because the row is prdb's record of the
    /// image and the bytes are the disposable part.
    /// </remarks>
    private async Task<int> EvictAsync(
        List<Cached> evictable,
        long over,
        CancellationToken cancellationToken)
    {
        var freed = 0L;
        var dropped = new List<long>();

        foreach (var image in evictable
                     .OrderBy(image => image.LastServedAt ?? DateTimeOffset.MinValue)
                     .ThenBy(image => image.Id))
        {
            if (freed >= over)
            {
                break;
            }

            store.Delete(image.PrdbId);

            freed += image.Bytes;
            dropped.Add(image.Id);
        }

        foreach (var batch in dropped.Chunk(ABatch))
        {
            await context.CatalogueImages
                .Where(row => batch.Contains(row.Id))
                .ExecuteUpdateAsync(
                    row => row
                        .SetProperty(image => image.Cached, false)
                        .SetProperty(image => image.LastServedAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        return dropped.Count;
    }

    /// <summary>One file in the cache, by both of its names and its weight.</summary>
    private sealed record Cached(long Id, Guid PrdbId, DateTimeOffset? LastServedAt, long Bytes);
}

/// <summary>What one pass over the artwork cache did.</summary>
/// <param name="UnpinnedBytes">
/// What the unpinned half weighed before anything was dropped, which is the
/// figure <see cref="ArtworkCeiling.Bytes"/> bounds.
/// </param>
/// <param name="Pinned">How many files belong to pinned videos and are therefore untouchable.</param>
/// <param name="Evicted">How many unpinned files were dropped to hold the ceiling.</param>
/// <param name="Orphans">How many files the catalogue no longer named at all.</param>
public sealed record ArtworkSweep(long UnpinnedBytes, int Pinned, int Evicted, int Orphans)
{
    public static ArtworkSweep Nothing { get; } = new(0, 0, 0, 0);

    /// <summary>Whether the pass had anything to do, which is ADR 0032's question.</summary>
    public bool DidSomething => Evicted > 0 || Orphans > 0;
}
