using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Automation;

public sealed class AutomationTests
{
    [Fact]
    public async Task The_status_surface_schema_migrates_cleanly_to_wanted_automation()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: "StatusSurface");
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>()
                .PrepareAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var context = reading.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(20, (await context.Installation.SingleAsync(TestContext.Current.CancellationToken)).AutomaticDownloadCap);
        var admissions = await context.GateAdmissions
            .Where(row => row.Gate == BeforeDownloadGate.Name)
            .Select(row => row.Confidence)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.True(admissions.ToHashSet().SetEquals(
            [IdentificationConfidence.Probable, IdentificationConfidence.Strong, IdentificationConfidence.Exact]));
        Assert.Empty(await context.AutomationRules.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Changing_the_before_download_gate_only_requeues_the_decide_work_set()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database, pending: false);

        await using (var scope = database.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IdentificationSettings>();
            var before = await settings.ReadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(BeforeDownloadGateChoice.ThroughProbable, before.BeforeDownload);
            var saved = await settings.SaveAsync(
                BeforeDownloadGateChoice.ExactOnly,
                before.AfterDownload,
                TestContext.Current.CancellationToken);
            Assert.Equal(1, saved.ReleasesReconsidered);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var release = await context.Releases.AsTracking().SingleAsync(TestContext.Current.CancellationToken);
            release.Confidence = IdentificationConfidence.Probable;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var choice = new ReleaseChoice(
                release.Id, seeded.IndexerId, "Fixture", release.DerivedReleaseId,
                release.Title, release.Size, release.Confidence, 1, null);
            var answer = await scope.ServiceProvider.GetRequiredService<AutomaticEligibility>()
                .ForVideoAsync(seeded.VideoId, [choice], TestContext.Current.CancellationToken);
            Assert.True(release.AutomationPending);
            Assert.Equal(AutomationDecisionReason.ConfidenceGate, answer[release.Id].Reason);
            Assert.Empty(await context.Downloads.ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task The_decide_routine_uses_the_shared_submission_and_records_every_permitting_rule()
    {
        var sabnzbd = new SabnzbdHandler();
        var indexer = new NzbHandler();
        await using var database = await TestDatabase.CreateAsync(also: services =>
        {
            services.AddHttpClient(FabTransports.Sabnzbd)
                .ConfigurePrimaryHttpMessageHandler(() => sabnzbd);
            services.AddHttpClient(FabTransports.Indexers)
                .ConfigurePrimaryHttpMessageHandler(() => indexer);
        });
        var seeded = await SeedAsync(database, pending: true);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.SabnzbdUrl, "http://sabnzbd.invalid")
                .SetProperty(row => row.SabnzbdApiKey, "fixture")
                .SetProperty(row => row.SabnzbdCategory, "xxx"),
                TestContext.Current.CancellationToken);
            var settings = scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>();
            Assert.True((await settings.SaveRuleAsync(
                null, "Any size", true, null, null, [seeded.IndexerId],
                TestContext.Current.CancellationToken)).Saved);
            Assert.True((await settings.SaveRuleAsync(
                null, "Small enough", true, null, 2_000, [seeded.IndexerId],
                TestContext.Current.CancellationToken)).Saved);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
            Assert.Equal(1, result.ItemsHandled);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var download = await context.Downloads.SingleAsync(TestContext.Current.CancellationToken);
            Assert.False(download.OriginIsPerson);
            Assert.Equal("automatic-nzo", download.NzoId);
            Assert.Equal(
                ["Any size", "Small enough"],
                await context.DownloadOriginRules.OrderBy(row => row.RuleName)
                    .Select(row => row.RuleName)
                    .ToArrayAsync(TestContext.Current.CancellationToken));
            Assert.False((await context.Releases.SingleAsync(TestContext.Current.CancellationToken)).AutomationPending);
        }

        Assert.Equal(1, indexer.Requests);
        Assert.Equal(["get_cats", "addfile"], sabnzbd.Modes);
    }

    [Fact]
    public async Task The_cap_waits_and_rule_changes_requeue_the_same_durable_work_set()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database, pending: false);
        Guid ruleId;

        await using (var scope = database.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>();
            var saved = await settings.SaveRuleAsync(
                null, "Fixture", true, 500, 2_000, [seeded.IndexerId],
                TestContext.Current.CancellationToken);
            Assert.True(saved.Saved);
            ruleId = saved.RuleId!.Value;
            Assert.Equal(1, saved.Reconsidered);

            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.AutomaticDownloadCap, 1),
                TestContext.Current.CancellationToken);
            context.Downloads.Add(new DownloadRow
            {
                Id = Guid.NewGuid(),
                VideoId = Guid.NewGuid(),
                IndexerId = seeded.IndexerId,
                DerivedReleaseId = "other",
                SubmittedName = "other",
                State = DownloadState.Outstanding,
                OutstandingSince = database.Time.GetUtcNow(),
                OriginIsPerson = false,
                CreatedAt = database.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var routine = scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>();
            Assert.Equal(RunOutcome.Succeeded, (await routine.RunAsync(
                null, TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal(RunOutcome.Succeeded, (await routine.RunAsync(
                null, TestContext.Current.CancellationToken)).Outcome);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var release = await context.Releases.SingleAsync(TestContext.Current.CancellationToken);
            Assert.True(release.AutomationPending);
            Assert.Equal(AutomationDecisionReason.AutomaticDownloadCap, release.AutomationDecisionReason);
            Assert.Equal(1, await context.ReleasesNotDownloaded.CountAsync(
                row => row.Reason == nameof(AutomationDecisionReason.AutomaticDownloadCap),
                TestContext.Current.CancellationToken));
        }

        await using (var scope = database.Scope())
        {
            var release = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Releases.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            var choice = new ReleaseChoice(
                release.Id, seeded.IndexerId, "Fixture", release.DerivedReleaseId,
                release.Title, release.Size, release.Confidence, 1, null);
            var answer = await scope.ServiceProvider.GetRequiredService<AutomaticEligibility>()
                .ForVideoAsync(seeded.VideoId, [choice], TestContext.Current.CancellationToken);
            Assert.Equal(AutomationDecisionReason.AutomaticDownloadCap, answer[release.Id].Reason);
            Assert.True(answer[release.Id].Wait);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Releases.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.AutomationPending, false),
                TestContext.Current.CancellationToken);
            var settings = scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>();
            var changed = await settings.SaveRuleAsync(
                ruleId, "Fixture", false, 500, 2_000, [seeded.IndexerId],
                TestContext.Current.CancellationToken);
            Assert.True(changed.Saved);
            Assert.Equal(1, changed.Reconsidered);
            Assert.True((await context.Releases.SingleAsync(TestContext.Current.CancellationToken)).AutomationPending);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Downloads.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            Assert.True((await scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>()
                .SaveRuleAsync(
                    ruleId, "Fixture", true, 500, 2_000, [seeded.IndexerId],
                    TestContext.Current.CancellationToken)).Saved);
            await context.Indexers.Where(row => row.Id == seeded.IndexerId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            var visible = await scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>()
                .ReadRuleAsync(ruleId, TestContext.Current.CancellationToken);
            Assert.False(visible!.Enabled);
        }
    }

    [Fact]
    public async Task Deleting_a_rule_keeps_the_copied_origin_name()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database, pending: false);
        Guid ruleId;
        Guid downloadId;

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var saved = await scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>()
                .SaveRuleAsync(null, "Keep this name", true, null, null, [seeded.IndexerId],
                    TestContext.Current.CancellationToken);
            ruleId = saved.RuleId!.Value;
            downloadId = Guid.NewGuid();
            context.Downloads.Add(new DownloadRow
            {
                Id = downloadId,
                VideoId = seeded.VideoId,
                IndexerId = seeded.IndexerId,
                DerivedReleaseId = "release",
                SubmittedName = "release",
                State = DownloadState.Collected,
                OutstandingSince = database.Time.GetUtcNow(),
                OriginIsPerson = false,
                CreatedAt = database.Time.GetUtcNow(),
            });
            context.DownloadOriginRules.Add(new DownloadOriginRuleRow
            {
                Id = Guid.NewGuid(),
                DownloadId = downloadId,
                AutomationRuleId = ruleId,
                RuleName = "Keep this name",
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var preview = await scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>()
                .PreviewDeleteAsync(ruleId, TestContext.Current.CancellationToken);
            Assert.Equal(1, preview!.ExistingOrigins);
            await scope.ServiceProvider.GetRequiredService<AutomationRuleSettings>()
                .DeleteAsync(ruleId, TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var origins = await scope.ServiceProvider.GetRequiredService<DownloadOrigins>()
                .ForAsync([downloadId], TestContext.Current.CancellationToken);
            var member = Assert.Single(origins[downloadId].Rules);
            Assert.Null(member.RuleId);
            Assert.Equal("Keep this name", member.Name);
        }
    }

    [Fact]
    public async Task Stopping_an_automatic_download_requeues_the_next_ranked_release()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database, pending: false);
        var downloadId = Guid.NewGuid();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Downloads.Add(new DownloadRow
            {
                Id = downloadId,
                VideoId = seeded.VideoId,
                IndexerId = seeded.IndexerId,
                DerivedReleaseId = "attempt",
                SubmittedName = "attempt",
                State = DownloadState.Outstanding,
                OutstandingSince = database.Time.GetUtcNow(),
                OriginIsPerson = false,
                CreatedAt = database.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var verdict = await scope.ServiceProvider.GetRequiredService<DownloadBrowse>()
                .StopFollowingAsync([downloadId], TestContext.Current.CancellationToken);
            Assert.Equal(DownloadSelectionOutcome.Stopped, verdict.Outcome);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var download = await context.Downloads.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(DownloadState.Failed, download.State);
            Assert.Equal(DownloadCause.Abandoned, download.Cause);
            Assert.True((await context.Releases.SingleAsync(TestContext.Current.CancellationToken)).AutomationPending);
        }
    }

    private static async Task<Seeded> SeedAsync(TestDatabase database, bool pending)
    {
        var indexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000071");
        var videoId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000071");
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var indexer = new IndexerRow
        {
            Id = indexerId,
            Name = "Fixture",
            Url = "https://indexer.invalid/api",
            ApiKey = "fixture",
            Categories = "Adult",
            Enabled = true,
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = database.Time.GetUtcNow(),
            Rank = 1,
        };
        var video = new CatalogueVideoRow
        {
            PrdbId = videoId,
            Title = "A Wanted Video",
            NormalisedTitle = "a wanted video",
            CreatedAtUtc = database.Time.GetUtcNow(),
            UpdatedAtUtc = database.Time.GetUtcNow(),
        };
        context.AddRange(indexer, video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.WantedVideos.Add(new WantedVideoRow { VideoId = video.Id, SinceAt = database.Time.GetUtcNow() });
        context.Releases.Add(new ReleaseRow
        {
            IndexerId = indexerId,
            DerivedReleaseId = "release",
            RawGuid = "release",
            Title = "release",
            NormalisedTitle = "release",
            Size = 1_000,
            Categories = "[]",
            PostDate = database.Time.GetUtcNow(),
            PubDate = database.Time.GetUtcNow(),
            DownloadUrl = "https://indexer.invalid/nzb",
            FirstSeenAt = database.Time.GetUtcNow(),
            IdentificationState = IdentificationState.Matched,
            VideoId = video.Id,
            Confidence = IdentificationConfidence.Exact,
            MatchedBy = IdentificationRung.ReleaseName,
            AutomationPending = pending,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(videoId, video.Id, indexerId);
    }

    private sealed record Seeded(Guid VideoId, long LocalVideoId, Guid IndexerId);

    private sealed class NzbHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3]),
            });
        }
    }

    private sealed class SabnzbdHandler : HttpMessageHandler
    {
        public List<string> Modes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri?.Query ?? string.Empty);
            var mode = query["mode"] ?? string.Empty;
            Modes.Add(mode);
            var body = mode switch
            {
                "get_cats" => "{\"categories\":[\"xxx\"]}",
                "addfile" => "{\"status\":true,\"nzo_ids\":[\"automatic-nzo\"]}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
