using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Status;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Status;

public sealed class SyncCoverageStatusTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(14, false)]
    [InlineData(30, false)]
    [InlineData(90, false)]
    public async Task Minimum_and_optional_coverage_have_different_status_meanings_and_reads_stay_local(int coveredDays, bool gap)
    {
        var socket = new FakePrdbApi();
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        var id = Guid.NewGuid();
        await using (var seed = database.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Indexers.Add(new IndexerRow { Id = id, Name = "Synthetic", Enabled = true,
                LastVerdict = IndexerConnectionOutcome.Saved, DailyQueryBudget = 1000 });
            context.IndexerWalkStates.Add(new IndexerWalkStateRow { IndexerId = id,
                HeadLastSucceededAt = database.Time.GetUtcNow(), MinimumPassQueries = 20 });
            foreach (var days in new[] { 14, 30, 90 })
                context.IndexerWindows.Add(new IndexerWindowRow { IndexerId = id, Days = days,
                    CompletedAt = days <= coveredDays ? database.Time.GetUtcNow() : null,
                    ProofStartedAt = days <= coveredDays ? database.Time.GetUtcNow() : null });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var scope = database.Scope();
        var status = await scope.ServiceProvider.GetRequiredService<StatusService>().ReadAsync(TestContext.Current.CancellationToken);
        var stage = status.Stages.Single(s => s.Id == "sync-indexers");
        Assert.Equal(gap, stage.Gaps.Any(g => g.Title.Contains("14-day minimum", StringComparison.Ordinal)));
        Assert.DoesNotContain(stage.Gaps, g => g.Title.Contains("90", StringComparison.Ordinal));
        Assert.Contains(stage.Facts, f => f.Label == "Synthetic: 90 days");
        Assert.Contains(stage.Facts, f => f.Label == "Synthetic head poll" && f.Value.Contains("succeeded", StringComparison.Ordinal));
        Assert.Empty(socket.Requests);
    }

    [Fact]
    public async Task An_impossible_budget_and_stale_quota_readings_are_explicit_and_secret_free()
    {
        var socket = new FakePrdbApi();
        await using var database = await TestDatabase.CreateAsync(prdb: socket);
        var governor = database.Services.GetRequiredService<PrdbGovernor>();
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        foreach (var (header, value) in new[] { ("Limit-Hour",100), ("Remaining-Hour",0), ("Reset-Hour",2),
            ("Limit-Month",2000), ("Remaining-Month",500), ("Reset-Month",5) })
            response.Headers.TryAddWithoutValidation("X-RateLimit-" + header, value.ToString());
        governor.Observe(response);
        governor.Costs.Request(PrdbWork.ReleaseIdentification, "/private/secret-name?apikey=secret-key", 200, TimeSpan.FromSeconds(2), 10);
        database.Time.Advance(TimeSpan.FromMinutes(2));
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var id = Guid.NewGuid();
        context.Indexers.Add(new IndexerRow { Id = id, Name = "Small", Enabled = true, LastVerdict = IndexerConnectionOutcome.Saved, DailyQueryBudget = 10 });
        context.IndexerWalkStates.Add(new IndexerWalkStateRow { IndexerId = id, MinimumPassQueries = 20 });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var status = await scope.ServiceProvider.GetRequiredService<StatusService>().ReadAsync(TestContext.Current.CancellationToken);
        Assert.Contains(status.Stages.Single(s => s.Id == "sync-indexers").Gaps,
            g => g.Title.Contains("cannot sustain", StringComparison.Ordinal) && g.Detail.Contains("106", StringComparison.Ordinal));
        var facts = status.Stages.Single(s => s.Id == "sync-prdb").Facts;
        Assert.Contains(facts, f => f.Label == "Rolling monthly account budget" && f.Value.Contains("stale", StringComparison.Ordinal));
        Assert.Contains(facts, f => f.Label == "Hourly account budget" && f.Value.Contains("not a full reset", StringComparison.Ordinal));
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(status), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(socket.Requests);
    }
}
