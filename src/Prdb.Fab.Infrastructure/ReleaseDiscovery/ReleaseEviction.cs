using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Holds one Indexer's cache ceiling without discarding an unexamined or pinned Release.</summary>
public sealed class ReleaseEviction(
    FabDbContext context,
    ReleasePins pins,
    ILogger<ReleaseEviction> logger)
{
    public async Task<ReleaseEvictionResult> EvictAsync(
        Guid indexerId,
        int ceiling = IndexerCacheCeiling.Rows,
        CancellationToken cancellationToken = default)
    {
        var held = await context.Releases.CountAsync(
            release => release.IndexerId == indexerId,
            cancellationToken);
        var over = IndexerCacheCeiling.OverBy(held, ceiling);
        if (over == 0)
        {
            return new(held, Removed: 0, OverBy: 0);
        }

        var disposable = pins.Unpinned(context.Releases.Where(release =>
                release.IndexerId == indexerId
                && release.IdentificationState != IdentificationState.Unexamined))
            .OrderBy(release => release.FirstSeenAt)
            .ThenBy(release => release.Id)
            .Select(release => release.Id)
            .Take(over);
        var ids = await disposable.ToListAsync(cancellationToken);

        var removed = ids.Count == 0
            ? 0
            : await context.Releases
                .Where(release => ids.Contains(release.Id))
                .ExecuteDeleteAsync(cancellationToken);
        var remainingOver = over - removed;

        if (remainingOver > 0)
        {
            logger.LogWarning(
                "The Indexer Cache holds {Held} Release(s), {Over} over its ceiling, and no safe disposal can hold the bound.",
                held - removed,
                remainingOver);
        }
        else
        {
            logger.LogInformation(
                "Evicted {Removed} disposable Release(s) from the Indexer Cache in first-seen order.",
                removed);
        }

        return new(held, removed, remainingOver);
    }
}

public sealed record ReleaseEvictionResult(int Held, int Removed, int OverBy);
