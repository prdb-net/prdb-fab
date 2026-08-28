using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class IndexerSearch(FabDbContext context, NewznabGateway gateway, TimeProvider time)
{
    public async Task<IndexerSearchRead> PageAsync(
        Guid indexerId,
        int page,
        int? maxAgeDays,
        CancellationToken cancellationToken)
    {
        var indexer = await context.Indexers.SingleAsync(row => row.Id == indexerId, cancellationToken);
        var state = await context.IndexerWalkStates.AsTracking().SingleAsync(row => row.IndexerId == indexerId, cancellationToken);
        var now = time.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        if (state.QueryDay != today)
        {
            state.QueryDay = today;
            state.QueriesSpentToday = 0;
        }

        if (state.QueriesSpentToday >= indexer.DailyQueryBudget)
        {
            return IndexerSearchRead.Deferred(today.AddDays(1) - now);
        }

        state.QueriesSpentToday++;
        await context.SaveChangesAsync(cancellationToken);

        var read = await gateway.SearchAsync(
            indexer.Url,
            indexer.ApiKey,
            DiscoveryState.DeserialiseIds(state.ResolvedCategoryIds),
            page * NewznabGateway.PageSize,
            maxAgeDays,
            cancellationToken: cancellationToken);

        return new(read, null);
    }
}

public sealed record IndexerSearchRead(NewznabSearchRead? Read, TimeSpan? DeferredFor)
{
    public static IndexerSearchRead Deferred(TimeSpan wait) => new(null, wait);
}
