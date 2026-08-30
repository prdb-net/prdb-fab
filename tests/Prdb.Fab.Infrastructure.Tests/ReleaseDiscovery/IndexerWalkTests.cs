using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed class IndexerWalkTests
{
    private static readonly Guid IndexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000002");

    [Fact]
    public async Task The_recent_window_commits_each_page_resumes_and_schedules_the_next_full_pass()
    {
        var remote = new PagingIndexer(offset => Feed(offset == 0 ? 100 : 1, offset));
        await using var database = await DatabaseAsync(remote);
        await SeedAsync(database);

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IndexerRecentWindowRoutine>()
                .RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
            Assert.Equal(100, result.ResultsSeen);
            Assert.Equal(100, result.RowsAdded);
            Assert.Equal(TimeSpan.Zero, result.DueIn);
        }

        await using (var scope = database.Scope())
        {
            var state = await scope.ServiceProvider.GetRequiredService<FabDbContext>().IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, state.RecentWindowResumePage);
            Assert.Null(state.RecentWindowCompletedAt);

            var result = await scope.ServiceProvider.GetRequiredService<IndexerRecentWindowRoutine>()
                .RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
            Assert.Equal(RecentWindow.CompleteEvery, result.DueIn);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(101, await context.Releases.CountAsync(TestContext.Current.CancellationToken));
            var state = await context.IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(state.RecentWindowCompletedAt);
            Assert.Equal(0, state.RecentWindowResumePage);
            Assert.True(await context.Routines.AnyAsync(
                row => row.Name == DiscoveryRoutineNames.RecentWindow,
                TestContext.Current.CancellationToken));
        }

        Assert.Equal(["0", "100"], remote.Requests.Select(uri => HttpUtility.ParseQueryString(uri.Query)["offset"]));
        Assert.All(remote.Requests, uri => Assert.Equal("90", HttpUtility.ParseQueryString(uri.Query)["maxage"]));
    }

    [Fact]
    public async Task An_old_backfilled_post_does_not_move_the_recurring_watermark()
    {
        var remote = new PagingIndexer(_ => Feed(1, 0, daysOld: 2));
        await using var database = await DatabaseAsync(remote);
        var watermark = database.Time.GetUtcNow();
        await SeedAsync(database, watermark);

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<IndexerWalkRoutine>()
                .RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ResultsSeen);
        }

        await using var check = database.Scope();
        var state = await check.ServiceProvider.GetRequiredService<FabDbContext>().IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(watermark, state.WatermarkPostDate);
    }

    [Fact]
    public async Task A_walk_run_records_results_seen_separately_from_rows_added()
    {
        var remote = new PagingIndexer(_ => Feed(3, 0));
        await using var database = await DatabaseAsync(remote);
        await SeedAsync(database, database.Time.GetUtcNow().AddMinutes(-15));

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var routine = scope.ServiceProvider.GetRequiredService<IndexerWalkRoutine>();
            var result = await routine.RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
            var row = await context.Routines.SingleAsync(
                item => item.Name == DiscoveryRoutineNames.Walk,
                TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<IRoutineStore>().RecordAsync(
                row.Id, result, routine.Cadence, TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        var run = await check.ServiceProvider.GetRequiredService<FabDbContext>().RoutineRuns.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, run.ResultsSeen);
        Assert.Equal(3, run.RowsAdded);
    }

    [Fact]
    public async Task Hitting_the_page_ceiling_opens_exactly_one_named_catch_up()
    {
        var remote = new PagingIndexer(offset => Feed(100, offset));
        await using var database = await DatabaseAsync(remote);
        await SeedAsync(database, database.Time.GetUtcNow().AddDays(-1));

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IndexerWalkRoutine>()
                .RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IndexerWalkRoutine>()
                .RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
        var state = await context.IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("missed paging window", state.CatchUpCause);
        Assert.Equal(1, await context.Routines.CountAsync(
            row => row.Name == DiscoveryRoutineNames.CatchUp,
            TestContext.Current.CancellationToken));
    }

    private static async Task<TestDatabase> DatabaseAsync(HttpMessageHandler remote) =>
        await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => remote));

    private static async Task SeedAsync(TestDatabase database, DateTimeOffset? watermark = null)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var now = database.Time.GetUtcNow();
        context.Indexers.Add(new IndexerRow
        {
            Id = IndexerId,
            Name = "Recorded",
            Url = "https://indexer.invalid/api",
            ApiKey = "indexer-key",
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = now,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<DiscoveryState>().InitialiseAsync(
            IndexerId,
            [new CapsCategory(5000, "Adult")],
            TestContext.Current.CancellationToken);

        if (watermark is not null)
        {
            await context.IndexerWalkStates.ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.WatermarkPostDate, watermark)
                    .SetProperty(row => row.WatermarkReleaseId, "held-before-this-run")
                    .SetProperty(row => row.RecentWindowCompletedAt, now),
                TestContext.Current.CancellationToken);
        }
    }

    private static string Feed(int count, int offset, int daysOld = 0)
    {
        var body = new StringBuilder("<?xml version=\"1.0\"?><rss xmlns:newznab=\"http://www.newznab.com/DTD/2010/feeds/attributes/\"><channel>");
        var date = new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero).AddDays(-daysOld);
        for (var index = 0; index < count; index++)
        {
            var id = $"release-{offset + index}";
            body.Append(CultureInfo.InvariantCulture, $"<item><title>{id}.mkv</title><guid>https://indexer.invalid/details/{id}</guid><link>https://indexer.invalid/get/{id}?apikey=indexer-key</link><pubDate>{date:R}</pubDate><newznab:attr name=\"usenetdate\" value=\"{date:O}\"/></item>");
        }
        return body.Append("</channel></rss>").ToString();
    }

    private sealed class PagingIndexer(Func<int, string> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var offset = int.Parse(query["offset"] ?? "0", CultureInfo.InvariantCulture);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(offset), Encoding.UTF8, "application/xml"),
            });
        }
    }
}
