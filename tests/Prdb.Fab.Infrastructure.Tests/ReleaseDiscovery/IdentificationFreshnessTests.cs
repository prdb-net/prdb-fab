using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed partial class IdentificationCutTests
{
    [Theory]
    [InlineData("exact")]
    [InlineData("ambiguous")]
    [InlineData("negative")]
    [InlineData("site")]
    public async Task Unchanged_proofs_preserve_original_answers_expiry_candidates_and_decisions_across_restart(string kind)
    {
        // The published server permits up to 24h. Its 6h default is covered separately.
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var expiry = at.AddHours(24);
        var original = FreshAnswer(kind, expiry);
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + original + "]}")
            .Answers("/videos/batch", VideoDetailsAnswer())
            .Answers("/videos/identify/validate", "{\"results\":[{\"ref\":\"1\",\"status\":\"unchanged\",\"result\":null}]}");
        await using var db = await FreshDatabaseAsync(socket);
        await IdentifyTurnAsync(db);
        long candidates;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var row = await context.Releases.AsTracking().SingleAsync(TestContext.Current.CancellationToken);
            row.AutomationPending = false;
            row.AutomationDecisionReason = AutomationDecisionReason.ConfidenceGate;
            row.AutomationDecisionAt = at;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            candidates = await context.ReleaseCandidates.CountAsync(TestContext.Current.CancellationToken);
        }
        db.Time.Advance(TimeSpan.FromHours(23));
        await IdentifyTurnAsync(db);
        await IdentifyTurnAsync(db); // another process scope has no repeated due work
        Assert.Single(socket.AskingFor("/videos/identify"));
        var validation = Assert.Single(socket.AskingFor("/videos/identify/validate"));
        using (var body = JsonDocument.Parse(validation.Body))
        {
            Assert.True(body.RootElement.GetProperty("reidentify").GetBoolean());
            Assert.True(body.RootElement.GetProperty("includeFreshness").GetBoolean());
            Assert.Equal("proof", body.RootElement.GetProperty("files")[0].GetProperty("freshnessToken").GetString());
        }
        await using var after = db.Scope();
        var contextAfter = after.ServiceProvider.GetRequiredService<FabDbContext>();
        var result = await contextAfter.Releases.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(at, result.LastIdentifiedAt);
        Assert.Equal(db.Time.GetUtcNow(), result.IdentificationValidatedAt);
        Assert.Equal(candidates, await contextAfter.ReleaseCandidates.CountAsync(TestContext.Current.CancellationToken));
        Assert.False(result.AutomationPending);
        Assert.Equal(AutomationDecisionReason.ConfidenceGate, result.AutomationDecisionReason);
        Assert.Equal(at, result.AutomationDecisionAt);
        using var json = JsonDocument.Parse(result.IdentificationAnswerJson!);
        Assert.Equal(expiry, json.RootElement.GetProperty("freshnessExpiresAtUtc").GetDateTimeOffset());
        Assert.Equal(1, db.Services.GetRequiredService<PrdbGovernor>().Costs.Snapshot().UnchangedInputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_or_unsupported_proofs_take_bounded_reidentification_without_extending_old_expiry(bool unsupported)
    {
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + FreshAnswer("negative", at.AddHours(unsupported ? 24 : 6)) + "]}");
        if (unsupported)
            socket.Refuses("/videos/identify/validate", HttpStatusCode.NotFound, "Older server")
                .Answers("/videos/identify", "{\"results\":[{\"ref\":\"1\",\"confidence\":0}]}");
        else socket.Answers("/videos/identify", "{\"results\":[" + FreshAnswer("negative", at.AddHours(29)).Replace("\"proof\"", "\"replacement\"") + "]}");
        await using var db = await FreshDatabaseAsync(socket);
        await IdentifyTurnAsync(db);
        db.Time.Advance(TimeSpan.FromHours(23));
        await IdentifyTurnAsync(db);
        Assert.Equal(unsupported ? 1 : 0, socket.AskingFor("/videos/identify/validate").Count);
        Assert.Equal(2, socket.AskingFor("/videos/identify").Count);
        await using var after = db.Scope();
        var row = await after.ServiceProvider.GetRequiredService<FabDbContext>().Releases.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(db.Time.GetUtcNow(), row.LastIdentifiedAt);
        Assert.Equal(db.Time.GetUtcNow(), row.IdentificationValidatedAt);
        Assert.DoesNotContain("\"proof\"", row.IdentificationAnswerJson!);
    }

    [Fact]
    public async Task A_replacement_is_checkpointed_before_its_details_are_deferred_and_is_not_validated_twice()
    {
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + FreshAnswer("negative", at.AddHours(24)) + "]}")
            .Answers("/videos/identify/validate", "{\"results\":[{\"ref\":\"1\",\"status\":\"identified\",\"result\":{\"ref\":\"1\",\"videoId\":\"" + MatchedVideo + "\",\"confidence\":4,\"matchedBy\":3}}]}")
            .Answers("/videos/batch", VideoDetailsAnswer());
        await using var db = await FreshDatabaseAsync(socket);
        await IdentifyTurnAsync(db);
        db.Time.Advance(TimeSpan.FromHours(23));
        socket.BeforeReply = ask =>
        {
            if (ask.Uri.AbsolutePath == "/videos/identify/validate") socket.Hourly = (100, 20, 60);
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<PrdbDeferredException>(() => IdentifyTurnAsync(db));
        await using (var scope = db.Scope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Releases.SingleAsync(TestContext.Current.CancellationToken);
            Assert.True(row.IdentificationDetailsPending);
            Assert.Equal(IdentificationState.Awaiting, row.IdentificationState);
            Assert.Contains(MatchedVideo.ToString(), row.IdentificationAnswerJson!);
            Assert.Equal(db.Time.GetUtcNow(), row.LastIdentifiedAt);
        }
        socket.BeforeReply = null;
        socket.Hourly = (1000000, 999999, 1);
        using (var response = new HttpResponseMessage(HttpStatusCode.OK))
        {
            response.Headers.Add("X-RateLimit-Limit-Hour", "1000000");
            response.Headers.Add("X-RateLimit-Remaining-Hour", "999999");
            response.Headers.Add("X-RateLimit-Reset-Hour", "1");
            db.Services.GetRequiredService<PrdbGovernor>().Observe(response);
        }
        await IdentifyTurnAsync(db);
        Assert.Single(socket.AskingFor("/videos/identify/validate"));
        Assert.Single(socket.AskingFor("/videos/batch"));
    }

    [Fact]
    public async Task An_unknown_validation_status_preserves_the_last_committed_authority()
    {
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + FreshAnswer("negative", at.AddHours(24)) + "]}")
            .Answers("/videos/identify/validate", "{\"results\":[{\"ref\":\"1\",\"status\":\"future-status\",\"result\":null}]}");
        await using var db = await FreshDatabaseAsync(socket);
        await IdentifyTurnAsync(db);
        db.Time.Advance(TimeSpan.FromHours(23));
        await Assert.ThrowsAsync<InvalidOperationException>(() => IdentifyTurnAsync(db));
        await using var scope = db.Scope();
        var row = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Releases.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(at, row.IdentificationValidatedAt);
        Assert.Equal(at, row.LastIdentifiedAt);
    }

    [Fact]
    public async Task An_account_change_in_flight_discards_validation_and_never_sends_that_proof_to_the_new_account()
    {
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + FreshAnswer("negative", at.AddHours(24)) + "]}")
            .Answers("/videos/identify", "{\"results\":[{\"ref\":\"1\",\"confidence\":0}]}")
            .Answers("/videos/identify/validate", "{\"results\":[{\"ref\":\"1\",\"status\":\"unchanged\",\"result\":null}]}");
        await using var db = await FreshDatabaseAsync(socket);
        await IdentifyTurnAsync(db);
        db.Time.Advance(TimeSpan.FromHours(23));
        socket.BeforeReply = async ask =>
        {
            if (ask.Uri.AbsolutePath != "/videos/identify/validate") return;
            await using var scope = db.Scope();
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
                u => u.SetProperty(i => i.PrdbUserHash, "another-account").SetProperty(i => i.PrdbApiKey, "another-key"), TestContext.Current.CancellationToken);
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => IdentifyTurnAsync(db));
        socket.BeforeReply = null;
        await IdentifyTurnAsync(db);
        Assert.Equal(2, socket.AskingFor("/videos/identify").Count);
        Assert.DoesNotContain("freshnessToken", socket.AskingFor("/videos/identify")[1].Body);
    }

    [Fact]
    public async Task Stale_identification_cannot_acquire_until_the_scheduled_validation_has_reestablished_authority()
    {
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var socket = new FakePrdbApi().Answers("/videos/identify", "{\"results\":[" + FreshAnswer("exact", at.AddHours(24)) + "]}")
            .Answers("/videos/batch", VideoDetailsAnswer())
            .Answers("/videos/identify/validate", "{\"results\":[{\"ref\":\"1\",\"status\":\"unchanged\",\"result\":null}]}");
        await using var db = await FreshDatabaseAsync(socket);
        await IdentifyTurnAsync(db);
        db.Time.Advance(TimeSpan.FromHours(23));
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Releases.ExecuteUpdateAsync(u => u.SetProperty(r => r.AutomationPending, true), TestContext.Current.CancellationToken);
            Assert.Null((await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>().RunAsync(null, TestContext.Current.CancellationToken)).Outcome);
            Assert.True((await context.Releases.SingleAsync(TestContext.Current.CancellationToken)).AutomationPending);
            var rankings = scope.ServiceProvider.GetRequiredService<ReleaseRankings>();
            Assert.Empty((await rankings.ForVideoAsync(MatchedVideo, false, TestContext.Current.CancellationToken))!.Ranked);
            var verdict = await scope.ServiceProvider.GetRequiredService<PersonDownloads>().DownloadAsync(
                Guid.NewGuid(), MatchedVideo, 1, TestContext.Current.CancellationToken);
            Assert.Contains("fresh prdb authority", verdict!.Detail);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads.ToListAsync(TestContext.Current.CancellationToken));
        }
        await IdentifyTurnAsync(db);
        await using var after = db.Scope();
        Assert.Single((await after.ServiceProvider.GetRequiredService<ReleaseRankings>().ForVideoAsync(
            MatchedVideo, false, TestContext.Current.CancellationToken))!.Ranked);
    }

    [Fact]
    public async Task Mixed_valid_changed_missing_and_no_fallback_results_keep_refs_and_use_only_a_bounded_fallback()
    {
        var at = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var answer = FreshAnswer("negative", at.AddHours(24));
        var initial = "{\"results\":[" + answer + "," + answer.Replace("\"ref\":\"1\"", "\"ref\":\"2\"")
            + "," + answer.Replace("\"ref\":\"1\"", "\"ref\":\"3\"") + ",{\"ref\":\"4\",\"confidence\":0}]}";
        var socket = new FakePrdbApi().Answers("/videos/identify", initial)
            .Answers("/videos/identify", "{\"results\":[{\"ref\":\"3\",\"confidence\":0}]}")
            .Answers("/videos/identify/validate", "{\"results\":["
                + "{\"ref\":\"1\",\"status\":\"unchanged\",\"result\":null},"
                + "{\"ref\":\"2\",\"status\":\"identified\",\"result\":{\"ref\":\"2\",\"confidence\":0}},"
                + "{\"ref\":\"3\",\"status\":\"reidentify\",\"result\":null},"
                + "{\"ref\":\"4\",\"status\":\"identified\",\"result\":{\"ref\":\"4\",\"confidence\":0}}]}");
        await using var db = await FreshDatabaseAsync(socket);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            for (var i = 2; i <= 4; i++)
                context.Releases.Add(Release("mixed-" + i, "Mixed.Input." + i, IdentificationState.Awaiting, at));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await IdentifyTurnAsync(db);
        db.Time.Advance(TimeSpan.FromHours(23));
        await IdentifyTurnAsync(db);
        Assert.Single(socket.AskingFor("/videos/identify/validate"));
        using var fallback = JsonDocument.Parse(socket.AskingFor("/videos/identify")[1].Body);
        Assert.Equal("3", Assert.Single(fallback.RootElement.GetProperty("files").EnumerateArray()).GetProperty("ref").GetString());
        await using var after = db.Scope();
        var rows = await after.ServiceProvider.GetRequiredService<FabDbContext>().Releases.OrderBy(r => r.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(at, rows[0].LastIdentifiedAt);
        Assert.All(rows.Skip(1), r => Assert.Equal(db.Time.GetUtcNow(), r.LastIdentifiedAt));
        Assert.All(rows, r => Assert.Equal(db.Time.GetUtcNow(), r.IdentificationValidatedAt));
    }

    private static string FreshAnswer(string kind, DateTimeOffset expiry) =>
        "{\"ref\":\"1\",\"freshnessToken\":\"proof\",\"freshnessExpiresAtUtc\":\"" + expiry.ToString("O") + "\"," + (kind switch
        {
            "exact" => "\"videoId\":\"" + MatchedVideo + "\",\"confidence\":4,\"matchedBy\":3",
            "ambiguous" => "\"confidence\":5,\"candidates\":[\"" + FirstCandidate + "\"]",
            "site" => "\"confidence\":0,\"site\":{\"id\":\"" + IdentifiedSite + "\",\"title\":\"Site\"}",
            _ => "\"confidence\":0",
        }) + "}";

    private static async Task<TestDatabase> FreshDatabaseAsync(FakePrdbApi socket)
    {
        var db = await TestDatabase.CreateAsync(prdb: socket);
        await SeedIndexerAsync(db, IndexerId);
        await SetPrdbKeyAsync(db);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var release = Release("proof-release", "Proof.Release", IdentificationState.Awaiting, db.Time.GetUtcNow());
        release.DownloadUrl = "https://indexer.invalid/nzb";
        context.Releases.Add(release);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }
}
