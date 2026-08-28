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
        CancellationToken cancellationToken)
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
                    .SetProperty(state => state.SweepQueriesSpentToday, 0),
                cancellationToken);

        var wantedTitles = purpose == IndexerQueryPurpose.Walk
            ? await context.WantedVideos.Select(row => row.Video!.Title).ToListAsync(cancellationToken)
            : [];
        var sweepHasWork = purpose == IndexerQueryPurpose.WantedSweep
            || wantedTitles.Select(WantedSearchTitle.Of).Any(WantedSearchTitle.IsSearchable);

        var reserve = sweepHasWork
            ? IndexerQueryBudget.ReservedForSweep(indexer.DailyQueryBudget)
            : 0;
        var admitted = purpose switch
        {
            IndexerQueryPurpose.WantedSweep => await context.IndexerWalkStates
                .Where(state => state.IndexerId == indexerId
                                && state.QueriesSpentToday < indexer.DailyQueryBudget
                                && state.SweepQueriesSpentToday < reserve)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(state => state.QueriesSpentToday, state => state.QueriesSpentToday + 1)
                        .SetProperty(state => state.SweepQueriesSpentToday, state => state.SweepQueriesSpentToday + 1),
                    cancellationToken),
            IndexerQueryPurpose.Walk => await context.IndexerWalkStates
                .Where(state => state.IndexerId == indexerId
                                && state.QueriesSpentToday < indexer.DailyQueryBudget
                                && state.QueriesSpentToday - state.SweepQueriesSpentToday
                                < indexer.DailyQueryBudget - reserve)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(
                        state => state.QueriesSpentToday,
                        state => state.QueriesSpentToday + 1),
                    cancellationToken),
            _ => 0,
        };

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
            page * NewznabGateway.PageSize,
            maxAgeDays,
            query,
            cancellationToken: cancellationToken);

        return new(read, null);
    }
}

public sealed record IndexerSearchRead(NewznabSearchRead? Read, TimeSpan? DeferredFor)
{
    public static IndexerSearchRead Deferred(TimeSpan wait) => new(null, wait);
}
