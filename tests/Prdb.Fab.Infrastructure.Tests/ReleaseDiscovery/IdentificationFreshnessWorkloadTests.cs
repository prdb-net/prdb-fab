using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

/// <summary>Actual SDK/Governor/SQLite turns, with matching evidence changing between turns.</summary>
public sealed class IdentificationFreshnessWorkloadTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Churn_and_proof_expiry_have_measured_costs_without_inventing_quota_savings()
    {
        var reports = new List<object>();
        foreach (var hours in new[] { 6, 24 })
        {
            var previous = await ExerciseAsync(hours, validate: false);
            var current = await ExerciseAsync(hours, validate: true);
            Assert.Equal(7, previous.Requests);
            Assert.Equal(previous.Requests, current.Requests); // validation is a metered batch too
            Assert.True(current.MonthlyRemaining < 180000); // includes the other client's continuing use
            if (hours == 6) Assert.Equal(previous.Identified, current.Identified);
            else
            {
                Assert.True(current.Unchanged > 0);
                Assert.True(current.Identified < previous.Identified);
                Assert.True(current.Bytes < previous.Bytes);
            }
            reports.Add(new { proofLifetimeHours = hours, inputs = 40, turns = 7, intervalHours = 23,
                changingInputsPerTurn = 1, competingClientRequestsPerDay = 4000, previous, current });
        }
        if (Environment.GetEnvironmentVariable("FAB_FRESHNESS_REPORT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }) + "\n", Token);
        }
    }

    private static async Task<Measured> ExerciseAsync(int lifetimeHours, bool validate)
    {
        var socket = new ChangingEvidence(lifetimeHours);
        await using var db = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => socket));
        socket.Clock = db.Time.GetUtcNow;
        var indexerId = Guid.NewGuid();
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Indexers.Add(new IndexerRow { Id = indexerId, Name = "Fixture", Url = "https://indexer.invalid", ApiKey = "fixture",
                Enabled = true, DailyQueryBudget = 1000, LastVerdict = IndexerConnectionOutcome.Saved });
            await context.Installation.ExecuteUpdateAsync(u => u.SetProperty(i => i.PrdbApiKey, "fixture"), Token);
            context.Releases.AddRange(Enumerable.Range(0, 40).Select(i => new ReleaseRow
            {
                IndexerId = indexerId, DerivedReleaseId = i.ToString(), RawGuid = i.ToString(), Title = $"Fixture.{i}",
                NormalisedTitle = $"fixture {i}", Categories = "[]", PostDate = db.Time.GetUtcNow(),
                FirstSeenAt = db.Time.GetUtcNow(), IdentificationState = IdentificationState.Awaiting,
            }));
            await context.SaveChangesAsync(Token);
        }
        for (var turn = 0; turn < 7; turn++)
        {
            if (turn > 0)
            {
                db.Time.Advance(TimeSpan.FromHours(23));
                socket.Change(turn);
                if (!validate)
                {
                    await using var old = db.Scope();
                    var context = old.ServiceProvider.GetRequiredService<FabDbContext>();
                    foreach (var row in await context.Releases.AsTracking().ToListAsync(Token))
                    {
                        var answer = JsonNode.Parse(row.IdentificationAnswerJson!)!.AsObject();
                        answer.Remove("freshnessToken");
                        answer.Remove("freshnessExpiresAtUtc");
                        row.IdentificationAnswerJson = answer.ToJsonString();
                    }
                    await context.SaveChangesAsync(Token);
                }
            }
            await using var scope = db.Scope();
            await scope.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>().RunAsync(null, Token);
            Assert.False(await ReleaseIdentificationWork.Current(scope.ServiceProvider.GetRequiredService<FabDbContext>(), db.Time.GetUtcNow()).AnyAsync(Token));
        }
        var costs = db.Services.GetRequiredService<PrdbGovernor>().Costs.Snapshot();
        return new(socket.Metered, socket.Identified, socket.Unchanged,
            costs.Requests.Where(r => r.Endpoint is "Identify" or "IdentifyValidation").Sum(r => r.PayloadBytes), socket.MonthRemaining);
    }

    private sealed record Measured(int Requests, int Identified, int Unchanged, long Bytes, int MonthlyRemaining);

    private sealed class ChangingEvidence(int lifetimeHours) : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Name, int Revision, DateTimeOffset Expires)> proofs = [];
        private readonly int[] revisions = new int[40];
        private readonly DateTimeOffset start = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        public Func<DateTimeOffset> Clock { get; set; } = () => new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        public int Metered { get; private set; }
        public int Identified { get; private set; }
        public int Unchanged { get; private set; }
        public int MonthRemaining => 180000 - (int)(Clock() - start).TotalDays * 4000 - Metered;
        public void Change(int turn) => revisions[turn % revisions.Length]++;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/rate-limit") return Json(new { hourly = new { limit = 1000, remaining = 800, resetsInSeconds = 60 },
                monthly = new { limit = 200000, remaining = MonthRemaining, resetsInSeconds = 60 } });
            Assert.True(path is "/videos/identify" or "/videos/identify/validate");
            Metered++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var results = new List<object>();
            foreach (var file in body.RootElement.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("filename").GetString()!;
                var id = int.Parse(name.AsSpan("Fixture.".Length));
                var reference = file.GetProperty("ref").GetString()!;
                if (path.EndsWith("/validate", StringComparison.Ordinal))
                {
                    var proof = file.TryGetProperty("freshnessToken", out var p) ? p.GetString() : null;
                    if (proof is not null && proofs.TryGetValue(proof, out var held)
                        && held.Name == name && held.Revision == revisions[id] && held.Expires > Clock())
                    {
                        results.Add(new { @ref = reference, status = "unchanged", result = (object?)null });
                        Unchanged++;
                    }
                    else results.Add(new { @ref = reference, status = "identified", result = Issue(name, id, reference) });
                }
                else results.Add(Issue(name, id, reference));
            }
            return Json(new { results });
        }

        private object Issue(string name, int id, string reference)
        {
            Identified++;
            var proof = $"proof-{Identified}";
            var expires = Clock().AddHours(lifetimeHours);
            proofs[proof] = (name, revisions[id], expires);
            // An ordinary evidence change can turn a positive into a negative.
            if (revisions[id] % 2 == 1) return new { @ref = reference, confidence = 0, freshnessToken = proof, freshnessExpiresAtUtc = expires };
            var videoId = Guid.Parse($"aaaaaaaa-0000-4000-8000-{id + 1:D12}");
            return new { @ref = reference, videoId, confidence = 4, matchedBy = 3, freshnessToken = proof, freshnessExpiresAtUtc = expires,
                video = new { id = videoId, title = name, description = new string('x', 1024), createdAtUtc = start.AddDays(-1),
                    updatedAtUtc = Clock(), actors = new object[0], images = new object[0], preNames = new object[0] } };
        }
        private HttpResponseMessage Json(object value)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
            response.Headers.Add("X-RateLimit-Limit-Hour", "1000");
            response.Headers.Add("X-RateLimit-Remaining-Hour", "799");
            response.Headers.Add("X-RateLimit-Reset-Hour", "60");
            response.Headers.Add("X-RateLimit-Limit-Month", "200000");
            response.Headers.Add("X-RateLimit-Remaining-Month", MonthRemaining.ToString());
            response.Headers.Add("X-RateLimit-Reset-Month", "60");
            return response;
        }
    }
}
