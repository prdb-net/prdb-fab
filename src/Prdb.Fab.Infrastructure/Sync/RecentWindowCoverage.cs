using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>The local, read-only proof of the Recent Window guarantee.</summary>
public sealed class RecentWindowCoverage(FabDbContext context, TimeProvider time)
{
    public async Task<RecentWindowCoverageState> ReadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var freshSince = now - RecentWindow.StaleAfter;
        var recentSince = RecentWindow.BeginsAt(now);
        var dueBefore = now - RecentWindow.RevalidateAfter;
        var catalogue = await context.RecentWindowState
            .Select(row => new RecentWindowSource(
                row.CatalogueCompletedAt != null && row.CatalogueCompletedAt >= freshSince,
                row.CatalogueCompletedAt,
                row.CatalogueOldestCreatedAt,
                row.CataloguePassStartedAt != null))
            .SingleAsync(cancellationToken);

        var indexers = await (from indexer in context.Indexers
                              join state in context.IndexerWalkStates on indexer.Id equals state.IndexerId
                              where indexer.Enabled
                              orderby indexer.Rank, indexer.Name
                              select new RecentWindowIndexer(
                                  indexer.Id,
                                  indexer.Name,
                                  state.RecentWindowCompletedAt != null
                                      && state.RecentWindowCompletedAt >= freshSince,
                                  state.RecentWindowCompletedAt,
                                  state.RecentWindowOldestPostDate,
                                  state.RecentWindowPassStartedAt != null))
            .ToListAsync(cancellationToken);

        var catalogueDetailsDue = await context.CatalogueVideos.CountAsync(
            row => row.CreatedAtUtc >= recentSince && row.LastReadAt <= dueBefore,
            cancellationToken);
        var identificationsDue = await context.Releases.CountAsync(
            row => row.PostDate >= recentSince
                && (row.IdentificationState == IdentificationState.Awaiting
                    || row.LastIdentifiedAt == null
                    || row.LastIdentifiedAt <= dueBefore),
            cancellationToken);

        return new(
            RecentWindow.Days,
            catalogue.Complete
                && indexers.All(indexer => indexer.Complete)
                && catalogueDetailsDue == 0
                && identificationsDue == 0,
            catalogue,
            indexers,
            catalogueDetailsDue,
            identificationsDue);
    }
}

public sealed record RecentWindowCoverageState(
    int Days,
    bool Complete,
    RecentWindowSource Catalogue,
    IReadOnlyList<RecentWindowIndexer> Indexers,
    int CatalogueDetailsDue,
    int IdentificationsDue);

public sealed record RecentWindowSource(
    bool Complete,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? OldestProvedAt,
    bool PassInProgress);

public sealed record RecentWindowIndexer(
    Guid Id,
    string Name,
    bool Complete,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? OldestProvedAt,
    bool PassInProgress);
