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
