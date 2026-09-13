using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Holding the user-preview asset cache to
/// <see cref="UserPreviewContract.CacheBytes"/>: least-recently-served first,
/// and everything no row claims swept up.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here is pinned.</strong> That is the one structural
/// difference from <see cref="ArtworkEviction"/>, and it is deliberate: ADR 0061
/// refuses to let a held Video put its user previews outside the ceiling, for
/// the reason ADR 0060 narrowed the artwork pin. A Library that files a hundred
/// Videos would otherwise make this cache grow with the library rather than with
/// the browsing, and the half <c>VISION.md</c> calls disposable would stop being
/// disposable. What the Library needs beside a Video File it <em>copies</em>
/// there; the cache is where it came from, not where it lives.
/// </para>
/// <para>
/// <strong>An orphan is ordinary here rather than exceptional.</strong> Every
/// republished sprite leaves its previous version behind under a name nothing
/// claims, and so does every withdrawal and every expired interest. The disk is
/// the authority on what is there, for ADR 0033's reason: a stored size would
/// have one writer and no reader that would notice it drifting.
/// </para>
/// </remarks>
public sealed class PreviewAssetEviction(
    FabDbContext context,
    PreviewAssetStore store,
    ILogger<PreviewAssetEviction> logger)
{
    /// <summary>How many files one question about the database covers.</summary>
    public const int ABatch = 500;

    /// <summary>
    /// Takes the cache back under <paramref name="ceiling"/> and drops whatever
    /// no row claims at that version.
    /// </summary>
    public async Task<PreviewAssetSweep> SweepAsync(
        long ceiling = UserPreviewContract.CacheBytes,
        CancellationToken cancellationToken = default)
    {
        var onDisk = new Dictionary<(Guid PreviewId, string Version), long>();

        foreach (var file in store.Held())
        {
            // Both halves of a pair under one key: they are one asset, they are
            // fetched and committed together, and evicting one of them would
            // leave the other unserveable and still on the disk.
            var key = (file.PreviewId, file.Version);

            onDisk[key] = onDisk.TryGetValue(key, out var already) ? already + file.Bytes : file.Bytes;
        }

        if (onDisk.Count == 0)
        {
            return PreviewAssetSweep.Nothing;
        }

        var evictable = new List<Cached>();
        var orphans = new List<(Guid PreviewId, string Version)>();

        foreach (var batch in onDisk.Keys.Chunk(ABatch))
        {
            await ExamineAsync(batch, onDisk, evictable, orphans, cancellationToken);
        }

        foreach (var orphan in orphans)
        {
            store.Delete(orphan.PreviewId, orphan.Version);
        }

        var held = evictable.Sum(asset => asset.Bytes);
        var over = UserPreviewContract.OverBy(held, ceiling);
        var removed = over == 0 ? 0 : await EvictAsync(evictable, over, cancellationToken);

        if (removed > 0 || orphans.Count > 0)
        {
            logger.LogInformation(
                "The user preview cache dropped {Removed} asset(s) and {Orphans} nothing claimed, "
                + "leaving {Held} byte(s) of a {Ceiling}.",
                removed,
                orphans.Count,
                held,
                ceiling);
        }

        return new PreviewAssetSweep(held, removed, orphans.Count);
    }

    private async Task ExamineAsync(
        (Guid PreviewId, string Version)[] batch,
        Dictionary<(Guid, string), long> onDisk,
        List<Cached> evictable,
        List<(Guid, string)> orphans,
        CancellationToken cancellationToken)
    {
        var ids = batch.Select(file => file.PreviewId).Distinct().ToList();

        var known = await context.UserPreviews
            .Where(row => ids.Contains(row.PrdbId))
            .Select(row => new
            {
                row.Id,
                row.PrdbId,
                row.CachedVersion,
                row.LastServedAt,
                row.Shown,
            })
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        foreach (var file in batch)
        {
            // A file is claimed only by a row that is shown and whose committed
            // version is this one. A superseded version, a withdrawn preview and
            // a row that has gone are the same thing to the disk.
            if (!known.TryGetValue(file.PreviewId, out var row)
                || !row.Shown
                || !string.Equals(row.CachedVersion, file.Version, StringComparison.Ordinal))
            {
                orphans.Add(file);
                continue;
            }

            evictable.Add(new Cached(row.Id, file.PreviewId, file.Version, row.LastServedAt, onDisk[file]));
        }
    }

    /// <summary>
    /// Drops least-recently-served assets until <paramref name="over"/> bytes
    /// have gone.
    /// </summary>
    /// <remarks>
    /// An asset never served sorts first: it is in the cache because a fetch put
    /// it there and nothing has asked for it since. The row stays and only loses
    /// its committed version — the row is prdb's record of the preview, and the
    /// bytes are the disposable part.
    /// </remarks>
    private async Task<int> EvictAsync(
        List<Cached> evictable,
        long over,
        CancellationToken cancellationToken)
    {
        var freed = 0L;
        var dropped = new List<long>();

        foreach (var asset in evictable
                     .OrderBy(asset => asset.LastServedAt ?? DateTimeOffset.MinValue)
                     .ThenBy(asset => asset.Id))
        {
            if (freed >= over)
            {
                break;
            }

            store.Delete(asset.PreviewId, asset.Version);

            freed += asset.Bytes;
            dropped.Add(asset.Id);
        }

        foreach (var batch in dropped.Chunk(ABatch))
        {
            await context.UserPreviews
                .Where(row => batch.Contains(row.Id))
                .ExecuteUpdateAsync(
                    row => row
                        .SetProperty(preview => preview.CachedVersion, (string?)null)
                        .SetProperty(preview => preview.LastServedAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        return dropped.Count;
    }

    private sealed record Cached(
        long Id,
        Guid PreviewId,
        string Version,
        DateTimeOffset? LastServedAt,
        long Bytes);
}

/// <summary>What one pass over the user-preview asset cache did.</summary>
/// <param name="Bytes">What the cache weighed before anything was dropped.</param>
/// <param name="Evicted">How many assets were dropped to hold the ceiling.</param>
/// <param name="Orphans">
/// How many no row claimed: a superseded version, a withdrawal, an expired
/// interest, or a pair whose commit never happened.
/// </param>
public sealed record PreviewAssetSweep(long Bytes, int Evicted, int Orphans)
{
    public static PreviewAssetSweep Nothing { get; } = new(0, 0, 0);

    /// <summary>Whether the pass had anything to do, which is ADR 0032's question.</summary>
    public bool DidSomething => Evicted > 0 || Orphans > 0;
}
