using System.Globalization;
using System.Net;
using System.Text;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed partial class IndexerWalkTests
{
    [Fact]
    public async Task Coverage_extends_14_then_30_and_pauses_optional_history_for_downstream_work()
    {
        var remote = new RangeIndexer();
        await using var database = await TestDatabase.CreateAsync(prdb: new FakePrdbApi(), also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => remote));
        await SeedAsync(database);
        await RangeTurnAsync(database);
        await RangeTurnAsync(database);
        DateTimeOffset? minimumProof;
        await using (var check = database.Scope())
        {
            var windows = await check.ServiceProvider.GetRequiredService<FabDbContext>().IndexerWindows.ToListAsync(TestContext.Current.CancellationToken);
            minimumProof = windows.Single(w => w.Days == 14).CompletedAt;
            Assert.NotNull(minimumProof);
            Assert.Null(windows.Single(w => w.Days == 30).CompletedAt);
        }
        await RangeTurnAsync(database);
        await RangeTurnAsync(database);
        var beforeOptional = remote.Requests.Count;
        var paused = await RangeTurnAsync(database);
        Assert.Null(paused.Outcome);
        Assert.Contains("downstream", paused.Reason);
        Assert.Equal(beforeOptional, remote.Requests.Count);
        await using (var prime = database.Scope())
        {
            var context = prime.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(minimumProof, (await context.IndexerWindows.SingleAsync(w => w.Days == 14, TestContext.Current.CancellationToken)).CompletedAt);
            Assert.NotNull((await context.IndexerWindows.SingleAsync(w => w.Days == 30, TestContext.Current.CancellationToken)).CompletedAt);
            Assert.Equal(300, await context.Releases.CountAsync(TestContext.Current.CancellationToken));
            await prime.ServiceProvider.GetRequiredService<PrdbGateway>().EnsureBudgetAsync("prdb-key", TestContext.Current.CancellationToken);
        }
        for (var turn = 0; turn < 10; turn++)
        {
            await using (var drain = database.Scope())
                await drain.ServiceProvider.GetRequiredService<FabDbContext>().Releases.ExecuteUpdateAsync(
                    u => u.SetProperty(r => r.IdentificationState, IdentificationState.Unknown), TestContext.Current.CancellationToken);
            await RangeTurnAsync(database);
        }
        await using var final = database.Scope();
        var db = final.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.NotNull((await db.IndexerWindows.SingleAsync(w => w.Days == 90, TestContext.Current.CancellationToken)).CompletedAt);
        Assert.Equal(900, await db.Releases.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new[] { 14, 14, 30, 30 }, remote.Requests.Take(4).Select(u => int.Parse(HttpUtility.ParseQueryString(u.Query)["maxage"]!, CultureInfo.InvariantCulture)));
        // Growing the target continues from the minimum anchor, never page zero.
        Assert.Equal("130", HttpUtility.ParseQueryString(remote.Requests[2].Query)["offset"]);
    }

    [Fact]
    public async Task Head_polling_during_bootstrap_and_anchor_overlap_tolerate_offset_drift()
    {
        var remote = new RangeIndexer();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => remote));
        await SeedAsync(database);
        await RangeTurnAsync(database);
        remote.NewHeadRows = 3;
        await using (var head = database.Scope())
            await head.ServiceProvider.GetRequiredService<IndexerWalkRoutine>().RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
        await RangeTurnAsync(database);
        await using var check = database.Scope();
        var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.NotNull((await context.IndexerWindows.SingleAsync(w => w.Days == 14, TestContext.Current.CancellationToken)).CompletedAt);
        Assert.Equal(143, await context.Releases.CountAsync(TestContext.Current.CancellationToken));
        Assert.NotNull((await context.IndexerWalkStates.SingleAsync(TestContext.Current.CancellationToken)).HeadLastSucceededAt);
    }

    [Fact]
    public async Task Missing_anchor_restarts_the_proof_and_preserves_cached_rows()
    {
        var remote = new RangeIndexer();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => remote));
        await SeedAsync(database);
        await RangeTurnAsync(database);
        remote.DeletedHeadRows = 20;
        await RangeTurnAsync(database);
        await using var check = database.Scope();
        var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
        var minimum = await context.IndexerWindows.SingleAsync(w => w.Days == 14, TestContext.Current.CancellationToken);
        Assert.Null(minimum.CompletedAt);
        Assert.Equal(0, minimum.ResumeOffset);
        Assert.True(await context.Releases.CountAsync(TestContext.Current.CancellationToken) >= 100);
    }

    private static async Task<Prdb.Fab.Core.Scheduling.RunResult> RangeTurnAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        return await scope.ServiceProvider.GetRequiredService<IndexerRecentWindowRoutine>().RunAsync(IndexerId.ToString(), TestContext.Current.CancellationToken);
    }

    private sealed class RangeIndexer : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public int NewHeadRows { get; set; }
        public int DeletedHeadRows { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var days = int.Parse(query["maxage"] ?? "90", CultureInfo.InvariantCulture);
            var offset = int.Parse(query["offset"] ?? "0", CultureInfo.InvariantCulture);
            var ids = Enumerable.Range(-NewHeadRows, days * 10 + NewHeadRows).Where(i => i >= DeletedHeadRows || i < 0).Skip(offset).Take(100);
            var xml = new StringBuilder("<rss><channel>");
            foreach (var id in ids)
            {
                var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero).AddMinutes(-id * 144);
                xml.Append(CultureInfo.InvariantCulture, $"<item><title>Name.{id}</title><guid>id-{id}</guid><link>https://indexer.invalid/get/{id}</link><pubDate>{at:R}</pubDate></item>");
            }
            xml.Append("</channel></rss>");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/xml") });
        }
    }
}
