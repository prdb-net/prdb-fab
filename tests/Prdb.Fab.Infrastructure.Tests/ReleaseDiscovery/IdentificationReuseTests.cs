using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.ReleaseDiscovery;
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
    public async Task Exact_inputs_share_an_answer_across_indexers_and_restart_until_its_original_expiry(bool ambiguous)
    {
        var answer = "{\"results\":[{\"ref\":\"1\",\"confidence\":" + (ambiguous ? "5" : "0")
            + (ambiguous ? ",\"candidates\":[\"" + FirstCandidate + "\"]" : "") + "}]}";
        var socket = new FakePrdbApi().Answers("/videos/identify", answer)
            .Answers("/videos/batch", VideoDetailsAnswer());
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(database, IndexerId);
        await SeedIndexerAsync(database, OtherIndexerId);
        await SetPrdbKeyAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            var first = Release("first", "Exact.Release-Name", IdentificationState.Awaiting, database.Time.GetUtcNow());
            var second = Release("second", "Exact.Release-Name", IdentificationState.Awaiting, database.Time.GetUtcNow());
            second.IndexerId = OtherIndexerId;
            context.Releases.AddRange(first, second);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await IdentifyTurnAsync(database);
        var request = Assert.Single(socket.AskingFor("/videos/identify"));
        using (var body = JsonDocument.Parse(request.Body))
            Assert.Single(body.RootElement.GetProperty("files").EnumerateArray());
        database.Time.Advance(TimeSpan.FromHours(1));
        await using (var add = database.Scope())
        {
            var context = add.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.Add(Release("later", "Exact.Release-Name", IdentificationState.Awaiting, database.Time.GetUtcNow()));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await IdentifyTurnAsync(database);
        Assert.Single(socket.AskingFor("/videos/identify"));
        await using (var check = database.Scope())
        {
            var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
            var rows = await context.Releases.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.Equal(ambiguous ? IdentificationState.Ambiguous : IdentificationState.Unknown, r.IdentificationState));
            Assert.Single(rows.Select(r => r.LastIdentifiedAt).Distinct());
            if (ambiguous) Assert.Equal(3, await context.ReleaseCandidates.CountAsync(TestContext.Current.CancellationToken));
        }
        Assert.Equal(1, database.Services.GetRequiredService<PrdbGovernor>().Costs.Snapshot().IdentifiedInputs);
        Assert.Equal(2, database.Services.GetRequiredService<PrdbGovernor>().Costs.Snapshot().ReusedAnswers);
        // Reuse cannot extend the old answer's lifetime indefinitely.
        database.Time.Advance(TimeSpan.FromHours(23));
        await IdentifyTurnAsync(database);
        Assert.Equal(2, socket.AskingFor("/videos/identify").Count);
    }

    [Fact]
    public async Task Similar_normalised_names_remain_distinct_remote_inputs()
    {
        var socket = new FakePrdbApi().Answers("/videos/identify",
            "{\"results\":[{\"ref\":\"1\",\"confidence\":0},{\"ref\":\"2\",\"confidence\":0}]}");
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(Release("one", "A.Release", IdentificationState.Awaiting, database.Time.GetUtcNow()),
                Release("two", "A Release", IdentificationState.Awaiting, database.Time.GetUtcNow()));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await IdentifyTurnAsync(database);
        using var body = JsonDocument.Parse(Assert.Single(socket.AskingFor("/videos/identify")).Body);
        Assert.Equal(2, body.RootElement.GetProperty("files").GetArrayLength());
    }

    private static async Task IdentifyTurnAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
    }
}
