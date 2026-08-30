using System.Net;
using System.Text;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed class ManualSearchTests
{
    [Fact]
    public async Task A_search_is_durable_before_response_and_the_scheduler_owns_the_remote_read()
    {
        var indexer = new SearchIndexer();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers)
                .ConfigurePrimaryHttpMessageHandler(() => indexer));
        var seeded = await SeedAsync(database);

        Guid searchId;
        await using (var scope = database.Scope())
        {
            var verdict = await scope.ServiceProvider.GetRequiredService<ManualSearches>()
                .StartAsync(seeded.VideoPrdbId, null, TestContext.Current.CancellationToken);
            Assert.Equal(ManualSearchStartOutcome.Started, verdict.Outcome);
            searchId = Assert.IsType<Guid>(verdict.SearchId);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(ManualSearchIndexerState.Queued,
                (await context.ManualSearchIndexers.SingleAsync(TestContext.Current.CancellationToken)).State);
            Assert.True(await context.Routines.AnyAsync(row =>
                row.Name == DiscoveryRoutineNames.ManualSearch
                && row.Target == searchId.ToString("D"), TestContext.Current.CancellationToken));
            Assert.Empty(indexer.Requests);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ManualSearchRoutine>()
                .RunAsync(searchId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.NotNull(result.Outcome);
        }

        var request = Assert.Single(indexer.Requests);
        Assert.Equal("Studio Scene", HttpUtility.ParseQueryString(request.Query)["q"]);
        Assert.Equal("0", HttpUtility.ParseQueryString(request.Query)["offset"]);

        await using var check = database.Scope();
        var contextCheck = check.ServiceProvider.GetRequiredService<FabDbContext>();
        var release = await contextCheck.Releases.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationState.Awaiting, release.IdentificationState);
        Assert.True(release.SearchWasReason);
        Assert.True(await contextCheck.ManualSearchResults.AnyAsync(row =>
            row.SearchId == searchId && row.ReleaseId == release.Id,
            TestContext.Current.CancellationToken));
        Assert.False(await contextCheck.Routines.AnyAsync(row =>
            row.Name == DiscoveryRoutineNames.ManualSearch && row.Target == searchId.ToString("D"),
            TestContext.Current.CancellationToken));

        var view = await check.ServiceProvider.GetRequiredService<ManualSearches>()
            .ReadAsync(searchId, TestContext.Current.CancellationToken);
        Assert.NotNull(view);
        Assert.Equal(ManualSearchPhase.Identifying, view.Phase);
        Assert.Equal(1, view.Results.Awaiting);
    }

    [Fact]
    public async Task A_second_request_reuses_the_active_search()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database);
        await using var scope = database.Scope();
        var searches = scope.ServiceProvider.GetRequiredService<ManualSearches>();

        var first = await searches.StartAsync(seeded.VideoPrdbId, seeded.IndexerId, TestContext.Current.CancellationToken);
        var second = await searches.StartAsync(seeded.VideoPrdbId, null, TestContext.Current.CancellationToken);

        Assert.Equal(ManualSearchStartOutcome.Started, first.Outcome);
        Assert.Equal(ManualSearchStartOutcome.AlreadyRunning, second.Outcome);
        Assert.Equal(first.SearchId, second.SearchId);
    }

    [Fact]
    public async Task A_manual_search_waits_before_spending_the_wanted_sweep_reservation()
    {
        var indexer = new SearchIndexer();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers)
                .ConfigurePrimaryHttpMessageHandler(() => indexer));
        var seeded = await SeedAsync(database);
        Guid searchId;
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var queryDay = new DateTimeOffset(database.Time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
            await context.Indexers.Where(row => row.Id == seeded.IndexerId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.DailyQueryBudget, 10),
                    TestContext.Current.CancellationToken);
            await context.IndexerWalkStates.Where(row => row.IndexerId == seeded.IndexerId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.QueryDay, queryDay)
                    .SetProperty(row => row.QueriesSpentToday, 5),
                    TestContext.Current.CancellationToken);
            context.WantedVideos.Add(new WantedVideoRow
            {
                VideoId = seeded.VideoId,
                SinceAt = database.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            searchId = (await scope.ServiceProvider.GetRequiredService<ManualSearches>()
                .StartAsync(seeded.VideoPrdbId, null, TestContext.Current.CancellationToken)).SearchId!.Value;
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ManualSearchRoutine>()
                .RunAsync(searchId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.Null(result.Outcome);
            Assert.NotNull(result.DueIn);
        }

        Assert.Empty(indexer.Requests);
        await using var check = database.Scope();
        var part = await check.ServiceProvider.GetRequiredService<FabDbContext>()
            .ManualSearchIndexers.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ManualSearchIndexerState.Deferred, part.State);
    }

    [Fact]
    public async Task Expired_searches_and_their_schedule_rows_are_removed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database);
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<ManualSearches>()
                .StartAsync(seeded.VideoPrdbId, null, TestContext.Current.CancellationToken);
        }

        database.Time.Advance(TimeSpan.FromDays(8));
        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ManualSearchRetentionRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ItemsHandled);
        }

        await using var check = database.Scope();
        var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Empty(await context.ManualSearches.ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(await context.Routines.AnyAsync(row =>
            row.Name == DiscoveryRoutineNames.ManualSearch,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_indexer_refusal_is_retained_and_retry_only_queues_the_same_work()
    {
        var indexer = new SearchIndexer { Status = HttpStatusCode.Forbidden };
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers)
                .ConfigurePrimaryHttpMessageHandler(() => indexer));
        var seeded = await SeedAsync(database);
        Guid searchId;
        await using (var scope = database.Scope())
        {
            searchId = (await scope.ServiceProvider.GetRequiredService<ManualSearches>()
                .StartAsync(seeded.VideoPrdbId, null, TestContext.Current.CancellationToken)).SearchId!.Value;
            await scope.ServiceProvider.GetRequiredService<ManualSearchRoutine>()
                .RunAsync(searchId.ToString("D"), TestContext.Current.CancellationToken);
        }

        Assert.Single(indexer.Requests);
        await using (var retry = database.Scope())
        {
            var view = await retry.ServiceProvider.GetRequiredService<ManualSearches>()
                .ReadAsync(searchId, TestContext.Current.CancellationToken);
            var part = Assert.Single(view!.Indexers);
            Assert.Equal(ManualSearchIndexerState.Failed, part.State);
            Assert.True(part.CanRetry);
            var verdict = await retry.ServiceProvider.GetRequiredService<ManualSearches>()
                .RetryAsync(searchId, seeded.IndexerId, TestContext.Current.CancellationToken);
            Assert.Equal(ManualSearchRetryOutcome.Scheduled, verdict.Outcome);
        }
        Assert.Single(indexer.Requests);

        indexer.Status = HttpStatusCode.OK;
        await using (var resumed = database.Scope())
        {
            await resumed.ServiceProvider.GetRequiredService<ManualSearchRoutine>()
                .RunAsync(searchId.ToString("D"), TestContext.Current.CancellationToken);
        }
        Assert.Equal(2, indexer.Requests.Count);
    }

    private static async Task<Seeded> SeedAsync(TestDatabase database)
    {
        var indexerId = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Indexers.Add(new IndexerRow
        {
            Id = indexerId,
            Name = "Search Indexer",
            Url = "https://indexer.invalid/api",
            ApiKey = "key",
            Categories = "Adult",
            Enabled = true,
            DailyQueryBudget = 100,
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = database.Time.GetUtcNow(),
        });
        context.IndexerWalkStates.Add(new IndexerWalkStateRow
        {
            IndexerId = indexerId,
            CapsTree = "[]",
            ResolvedCategoryIds = "[]",
            MissingCategoryNames = "[]",
        });
        context.CatalogueVideos.Add(new CatalogueVideoRow
        {
            PrdbId = videoId,
            Title = "Studio Scene",
            NormalisedTitle = "studio scene",
            CreatedAtUtc = database.Time.GetUtcNow(),
            UpdatedAtUtc = database.Time.GetUtcNow(),
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var localVideoId = await context.CatalogueVideos.Where(row => row.PrdbId == videoId)
            .Select(row => row.Id).SingleAsync(TestContext.Current.CancellationToken);
        return new(indexerId, videoId, localVideoId);
    }

    private sealed record Seeded(Guid IndexerId, Guid VideoPrdbId, long VideoId);

    private sealed class SearchIndexer : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            const string body = """
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
                  <channel><item>
                    <title>Studio.Scene.1080p</title>
                    <guid>search-result</guid>
                    <link>https://indexer.invalid/get/search-result?apikey=key</link>
                    <pubDate>Thu, 27 Aug 2026 08:00:00 +0000</pubDate>
                    <newznab:attr name="category" value="5010" />
                  </item></channel>
                </rss>
                """;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            });
        }
    }
}
