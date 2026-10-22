using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>The local, read-only proof of the Recent Window guarantee.</summary>
public sealed class RecentWindowCoverage(FabDbContext context, TimeProvider time, VideoChangeCoverage changes)
{
    public async Task<RecentWindowCoverageState> ReadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var freshSince = now - RecentWindow.StaleAfter;
        var recentSince = RecentWindow.BeginsAt(now);
        var dueBefore = now - RecentWindow.RevalidateAfter;
        var catalogueState = await context.RecentWindowState.SingleAsync(cancellationToken);
        var live = catalogueState.BaselinePhase == "live" && await changes.FreshAsync(cancellationToken);
        var catalogue = new RecentWindowSource(
            live || catalogueState.BaselinePhase is not ("scan" or "catch-up" or "live")
                && catalogueState.CatalogueCompletedAt >= freshSince,
            catalogueState.CatalogueCompletedAt, catalogueState.CatalogueOldestCreatedAt,
            catalogueState.BaselinePhase is "scan" or "catch-up" || catalogueState.CataloguePassStartedAt != null,
            catalogueState.BaselinePhase, catalogueState.BaselinePages, catalogueState.BaselineDocuments);

        var legacyIndexers = await (from indexer in context.Indexers
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

        var ranges = await context.IndexerWindows.ToListAsync(cancellationToken);
        var walks = await context.IndexerWalkStates.ToListAsync(cancellationToken);
        var indexers = legacyIndexers.Select(indexer =>
        {
            var proved = new[] { 14, 30, 90 }.Select(days =>
            {
                var range = ranges.SingleOrDefault(row => row.IndexerId == indexer.Id && row.Days == days);
                return range is null
                    ? new IndexerRangeCoverage(days, indexer.Complete, indexer.CompletedAt, null, indexer.OldestProvedAt, 0, false)
                    : new IndexerRangeCoverage(days, IndexerRecentWindowRoutine.Fresh(range, now), range.CompletedAt,
                        range.ProofStartedAt, range.OldestPostDate, range.ResumeOffset, range.PassStartedAt != null);
            }).ToArray();
            var working = proved.Single(row => row.Days == 30);
            return indexer with { Complete = working.Fresh, CompletedAt = working.CompletedAt,
                Ranges = proved, HeadLastSucceededAt = walks.Single(row => row.IndexerId == indexer.Id).HeadLastSucceededAt };
        }).ToArray();

        var feedState = await context.VideoChangeState.SingleOrDefaultAsync(cancellationToken);
        var feedFresh = await changes.FreshAsync(cancellationToken);
        var lowerBound = feedState?.LowerBoundAt;
        var catalogueDetailsDue = await context.CatalogueVideos.CountAsync(
            row => row.CreatedAtUtc >= recentSince && (feedFresh
                ? row.ChangesBaselineAt != lowerBound || row.LastReadAt == default
                : row.LastReadAt <= dueBefore),
            cancellationToken);
        var pending = ReleaseIdentificationWork.Current(context, now, includeDelayed: true);
        var identificationsDue = await pending.CountAsync(cancellationToken);
        var distinctInputsDue = await pending.Select(row => row.Title).Distinct().CountAsync(cancellationToken);

        return new(
            RecentWindow.Days,
            catalogue.Complete
                && indexers.All(indexer => indexer.Complete)
                && catalogueDetailsDue == 0
                && identificationsDue == 0,
            catalogue,
            indexers,
            catalogueDetailsDue,
            identificationsDue,
            feedState is null ? null : new VideoChangeProgress(
                feedState.BaselineCompletedAt, feedState.CaughtUpAt,
                feedState.CaughtUpAt is { } at ? Math.Max(0, (now - at).TotalSeconds) : null,
                feedFresh),
            IndexerWindowPolicy.InitialDays,
            distinctInputsDue);
    }
}

public sealed record RecentWindowCoverageState(
    int Days,
    bool Complete,
    RecentWindowSource Catalogue,
    IReadOnlyList<RecentWindowIndexer> Indexers,
    int CatalogueDetailsDue,
    int IdentificationsDue,
    VideoChangeProgress? VideoChanges = null,
    int IndexerInitialDays = 30,
    int IdentificationInputsDue = 0);

public sealed record VideoChangeProgress(DateTimeOffset? BaselineCompletedAt, DateTimeOffset? CaughtUpAt,
    double? LagSeconds, bool Fresh);

public sealed record RecentWindowSource(
    bool Complete,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? OldestProvedAt,
    bool PassInProgress,
    string? BaselinePhase = null,
    int BaselinePages = 0,
    int BaselineDocuments = 0);

public sealed record RecentWindowIndexer(
    Guid Id,
    string Name,
    bool Complete,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? OldestProvedAt,
    bool PassInProgress,
    IReadOnlyList<IndexerRangeCoverage>? Ranges = null,
    DateTimeOffset? HeadLastSucceededAt = null);

public sealed record IndexerRangeCoverage(int Days, bool Fresh, DateTimeOffset? CompletedAt,
    DateTimeOffset? ProofStartedAt, DateTimeOffset? OldestProvedAt, int ResumeOffset, bool PassInProgress);
