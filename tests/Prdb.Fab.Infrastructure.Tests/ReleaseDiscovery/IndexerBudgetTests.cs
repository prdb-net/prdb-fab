using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed partial class IndexerWalkTests
{
    [Fact]
    public async Task Optional_history_stops_before_later_head_polls_and_the_minimum_budget()
    {
        var remote = new PagingIndexer(_ => Feed(0, 0));
        await using var database = await DatabaseAsync(remote);
        await SeedAsync(database);
        await using (var seed = database.Scope())
            await seed.ServiceProvider.GetRequiredService<FabDbContext>().IndexerWalkStates.ExecuteUpdateAsync(
                u => u.SetProperty(s => s.QueriesSpentToday, 900).SetProperty(s => s.MinimumPassQueries, 20), TestContext.Current.CancellationToken);
        var sent = 0;
        for (var i = 0; i < 50; i++)
        {
            await using var scope = database.Scope();
            var read = await scope.ServiceProvider.GetRequiredService<IndexerSearch>().PageAsync(IndexerId, 0, 90,
                IndexerQueryPurpose.OptionalHistory, null, TestContext.Current.CancellationToken);
            if (read.DeferredFor is not null) break;
            sent++;
        }
        // At 09:00 UTC, sixty head polls and twenty minimum queries remain.
        Assert.Equal(20, sent);
        database.Time.Advance(TimeSpan.FromMinutes(15));
        await using var next = database.Scope();
        var head = await next.ServiceProvider.GetRequiredService<IndexerSearch>().PageAsync(IndexerId, 0, null,
            IndexerQueryPurpose.Walk, null, TestContext.Current.CancellationToken);
        Assert.Null(head.DeferredFor);
        var minimum = await next.ServiceProvider.GetRequiredService<IndexerSearch>().PageAsync(IndexerId, 0, 14,
            IndexerQueryPurpose.MinimumWindow, null, TestContext.Current.CancellationToken);
        Assert.Null(minimum.DeferredFor);
    }

    [Fact]
    public async Task Concurrent_lanes_do_not_overspend_and_midnight_resets_all_purpose_counters()
    {
        var remote = new PagingIndexer(_ => Feed(0, 0));
        await using var database = await DatabaseAsync(remote);
        await SeedAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Indexers.ExecuteUpdateAsync(u => u.SetProperty(i => i.DailyQueryBudget, 6), TestContext.Current.CancellationToken);
        }
        var reads = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var scope = database.Scope();
            return await scope.ServiceProvider.GetRequiredService<IndexerSearch>().PageAsync(IndexerId, 0, null,
                IndexerQueryPurpose.Walk, null, TestContext.Current.CancellationToken);
        }));
        Assert.Equal(6, reads.Count(r => r.DeferredFor is null));
        database.Time.Advance(TimeSpan.FromDays(1));
        await using var next = database.Scope();
        await next.ServiceProvider.GetRequiredService<IndexerSearch>().PageAsync(IndexerId, 0, null,
            IndexerQueryPurpose.Walk, null, TestContext.Current.CancellationToken);
        var state = await next.ServiceProvider.GetRequiredService<FabDbContext>().IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, state.QueriesSpentToday);
        Assert.Equal(1, state.HeadQueriesSpentToday);
        Assert.Equal(0, state.HistoryQueriesSpentToday);
        Assert.Equal(0, state.MinimumQueriesSpentToday);
    }
}
