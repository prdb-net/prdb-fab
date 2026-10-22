using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed partial class IdentificationCutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_answers_survive_failed_or_deferred_details_and_resume_without_identifying_again(bool quota)
    {
        var socket = new FakePrdbApi().Answers("/videos/identify", IdentifyAnswer());
        if (!quota) socket.IsUnreachable("/videos/batch");
        if (quota) socket.Hourly = (1000, 500, 60);
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(Enumerable.Range(1, 4).Select(i =>
                Release($"release-{i}", $"Release.{i}", IdentificationState.Awaiting, database.Time.GetUtcNow())));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using (var first = database.Scope())
        {
            var routine = first.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>();
            if (quota)
                await Assert.ThrowsAsync<PrdbDeferredException>(() => routine.RunAsync(null, TestContext.Current.CancellationToken));
            else
                await Assert.ThrowsAsync<HttpRequestException>(() => routine.RunAsync(null, TestContext.Current.CancellationToken));
        }
        await using (var check = database.Scope())
        {
            var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
            var saved = await context.Releases.ToListAsync(TestContext.Current.CancellationToken);
            Assert.All(saved, row =>
            {
                Assert.True(row.IdentificationDetailsPending);
                Assert.NotNull(row.IdentificationAnswerJson);
                Assert.False(row.AutomationPending);
                Assert.Equal(IdentificationState.Awaiting, row.IdentificationState);
            });
            Assert.Null(await check.ServiceProvider.GetRequiredService<ReleaseRankings>()
                .ForVideoAsync(MatchedVideo, false, TestContext.Current.CancellationToken));
        }
        // A new scope is the restart seam: all result state comes from SQLite.
        socket.Hourly = (1000, 900, 60);
        socket.Answers("/user-identity", "{\"userHash\":\"test\"}");
        socket.Answers("/videos/batch", VideoDetailsAnswer());
        await using (var resumed = database.Scope())
        {
            await resumed.ServiceProvider.GetRequiredService<PrdbGateway>().CheckAsync(
                "prdb-key", TestContext.Current.CancellationToken);
            var result = await resumed.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(4, result.ItemsHandled);
        }
        Assert.Single(socket.AskingFor("/videos/identify"));
        await using var final = database.Scope();
        var finalContext = final.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(0, await finalContext.Releases.CountAsync(r => r.IdentificationDetailsPending, TestContext.Current.CancellationToken));
        Assert.Equal(2, await finalContext.ReleaseCandidates.CountAsync(TestContext.Current.CancellationToken));
        var idle = await final.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal(0, idle.ItemsHandled);
        Assert.Single(socket.AskingFor("/videos/identify"));
    }
}

public sealed partial class IdentificationCutTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inline_or_fresh_cached_detail_avoids_a_metered_followup(bool inline)
    {
        var videoJson = $$"""{ "id":"{{MatchedVideo}}", "title":"Prepared Video", "actors":[], "images":[], "preNames":[] }""";
        var socket = new FakePrdbApi().Answers("/videos/identify",
            "{\"results\":[{\"ref\":\"1\",\"videoId\":\"" + MatchedVideo + "\",\"confidence\":4,\"matchedBy\":3"
            + (inline ? ",\"video\":" + videoJson : "") + "}]}");
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.Add(Release("inline", "An.Exact.Release", IdentificationState.Awaiting, database.Time.GetUtcNow()));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            if (!inline)
                await seed.ServiceProvider.GetRequiredService<Prdb.Fab.Infrastructure.Sync.VideoDetails>().WriteAsync(
                    new Prdb.Sdk.Generated.Models.VideoDetailDto { Id = MatchedVideo, Title = "Prepared Video" },
                    TestContext.Current.CancellationToken);
        }
        await using var run = database.Scope();
        await run.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
        Assert.Empty(socket.AskingFor("/videos/batch"));
        var release = await run.ServiceProvider.GetRequiredService<FabDbContext>().Releases.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationState.Matched, release.IdentificationState);
        Assert.Equal(IdentificationConfidence.Exact, release.Confidence);
        Assert.False(release.IdentificationDetailsPending);
        Assert.Equal(1, database.Services.GetRequiredService<PrdbGovernor>().Costs.Snapshot().DetailFetchesAvoided);
    }

    [Fact]
    public async Task An_omitted_match_cannot_use_stale_details_as_current_evidence()
    {
        var socket = new FakePrdbApi().Answers("/videos/identify",
            "{\"results\":[{\"ref\":\"1\",\"videoId\":\"" + MatchedVideo + "\",\"confidence\":4,\"matchedBy\":3}]}")
            .Answers("/videos/batch", "[]");
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            context.CatalogueVideos.Add(new CatalogueVideoRow { PrdbId = MatchedVideo, Title = "Stale", LastReadAt = database.Time.GetUtcNow().AddDays(-2) });
            context.Releases.Add(Release("omitted", "An.Exact.Release", IdentificationState.Awaiting, database.Time.GetUtcNow()));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var run = database.Scope();
        await run.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
        var release = await run.ServiceProvider.GetRequiredService<FabDbContext>().Releases.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(release.IdentificationDetailsPending);
        Assert.Equal(IdentificationState.Awaiting, release.IdentificationState);
        Assert.False(release.AutomationPending);
        Assert.Empty((await run.ServiceProvider.GetRequiredService<ReleaseRankings>().ForVideoAsync(MatchedVideo, false, TestContext.Current.CancellationToken))!.Ranked);
        Assert.Single(socket.AskingFor("/videos/batch"));
    }
}
