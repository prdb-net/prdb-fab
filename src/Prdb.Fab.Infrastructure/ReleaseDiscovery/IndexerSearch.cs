using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class IndexerSearch(FabDbContext context, NewznabGateway gateway, TimeProvider time)
{
    public async Task<IndexerSearchRead> PageAsync(
        Guid indexerId,
        int page,
        int? maxAgeDays,
        IndexerQueryPurpose purpose,
        string? query,
        CancellationToken cancellationToken,
        int? offset = null)
    {
        var indexer = await context.Indexers.SingleAsync(row => row.Id == indexerId, cancellationToken);
        _ = await context.IndexerWalkStates
            .AsNoTracking()
            .SingleAsync(row => row.IndexerId == indexerId, cancellationToken);
        var now = time.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        // Walk and Wanted Sweep run in different lanes. Reset and admission
        // therefore use database updates rather than a read/change/write
        // counter that could lose one concurrent request.
        await context.IndexerWalkStates
            .Where(state => state.IndexerId == indexerId && state.QueryDay != today)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(state => state.QueryDay, today)
                    .SetProperty(state => state.QueriesSpentToday, 0)
                    .SetProperty(state => state.SweepQueriesSpentToday, 0)
                    .SetProperty(state => state.HeadQueriesSpentToday, 0)
                    .SetProperty(state => state.MinimumQueriesSpentToday, 0)
                    .SetProperty(state => state.HistoryQueriesSpentToday, 0),
                cancellationToken);

        var plan = await BudgetAsync(indexerId, cancellationToken);
        var allowance = plan.AvailableFor(purpose);
        var admitted = await context.IndexerWalkStates
            .Where(state => state.IndexerId == indexerId
                && state.QueryDay == today && state.QueriesSpentToday < plan.Spent + allowance)
            .ExecuteUpdateAsync(update => update
                .SetProperty(state => state.QueriesSpentToday, state => state.QueriesSpentToday + 1)
                .SetProperty(state => state.SweepQueriesSpentToday, state => state.SweepQueriesSpentToday + (purpose == IndexerQueryPurpose.WantedSweep ? 1 : 0))
                .SetProperty(state => state.HeadQueriesSpentToday, state => state.HeadQueriesSpentToday + (purpose == IndexerQueryPurpose.Walk ? 1 : 0))
                .SetProperty(state => state.MinimumQueriesSpentToday, state => state.MinimumQueriesSpentToday + (purpose == IndexerQueryPurpose.MinimumWindow ? 1 : 0))
                .SetProperty(state => state.HistoryQueriesSpentToday, state => state.HistoryQueriesSpentToday
                    + (purpose == IndexerQueryPurpose.OptionalHistory || purpose == IndexerQueryPurpose.WorkingWindow ? 1 : 0)), cancellationToken);

        if (admitted == 0)
        {
            return IndexerSearchRead.Deferred(today.AddDays(1) - now);
        }

        var state = await context.IndexerWalkStates
            .AsNoTracking()
            .SingleAsync(row => row.IndexerId == indexerId, cancellationToken);

        var read = await gateway.SearchAsync(
            indexer.Url,
            indexer.ApiKey,
            DiscoveryState.DeserialiseIds(state.ResolvedCategoryIds),
            offset ?? page * NewznabGateway.PageSize,
            maxAgeDays,
            query,
            cancellationToken: cancellationToken);

        return new(read, null);
    }
    public async Task<IndexerBudgetPlan> BudgetAsync(Guid indexerId, CancellationToken cancellationToken)
    {
        var indexer = await context.Indexers.SingleAsync(row => row.Id == indexerId, cancellationToken);
        var state = await context.IndexerWalkStates.SingleAsync(row => row.IndexerId == indexerId, cancellationToken);
        var now = time.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var currentDay = state.QueryDay == today;
        var titles = await context.WantedVideos.Select(row => row.Video!.Title).ToListAsync(cancellationToken);
        var wanted = titles.Select(WantedSearchTitle.Of).Count(WantedSearchTitle.IsSearchable);
        // A Manual Search is a bounded, durable request; unfinished pairs are
        // included even if the user made them after historical work started.
        var manual = await context.ManualSearchIndexers.CountAsync(row => row.IndexerId == indexerId
            && (row.State == ManualSearchIndexerState.Queued || row.State == ManualSearchIndexerState.Searching || row.State == ManualSearchIndexerState.Deferred), cancellationToken);
        var minimumProof = await context.IndexerWindows.SingleOrDefaultAsync(row => row.IndexerId == indexerId
            && row.Days == IndexerWindowPolicy.MinimumDays, cancellationToken);
        var measuredMinimum = Math.Max(state.MinimumPassQueries, minimumProof?.LastPassQueries ?? 0);
        var observedReleases = await context.Releases.CountAsync(row => row.IndexerId == indexerId
            && row.PostDate >= now.AddDays(-IndexerWindowPolicy.MinimumDays), cancellationToken);
        measuredMinimum = Math.Max(measuredMinimum, (int)Math.Ceiling(observedReleases / 90d));
        var minimum = minimumProof?.CompletedAt >= now.AddHours(-24)
            && minimumProof.ProofStartedAt >= now.AddHours(-24) ? 0
            : Math.Max(1, measuredMinimum - (currentDay ? state.MinimumQueriesSpentToday : 0));
        return IndexerQueryBudget.Plan(indexer.DailyQueryBudget, currentDay ? state.QueriesSpentToday : 0,
            now, state.HeadPagesPerPoll, minimum, wanted, manual);
    }

}

public sealed record IndexerSearchRead(NewznabSearchRead? Read, TimeSpan? DeferredFor)
{
    public static IndexerSearchRead Deferred(TimeSpan wait) => new(null, wait);
}
