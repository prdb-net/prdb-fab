using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Tests.Sync;
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

    [Fact]
    public async Task Prepared_collection_starts_at_most_100_missing_wanted_with_cap_20_then_only_new_intent()
    {
        var token = TestContext.Current.CancellationToken;
        var api = new FakePrdbApi();
        var sab = new SabnzbdHandler();
        await using var database = await TestDatabase.CreateAsync(prdb: api, also: services =>
        {
            services.AddSingleton<IProbeProcess>(new CollectionProbe());
            services.AddHttpClient(FabTransports.Sabnzbd).ConfigurePrimaryHttpMessageHandler(() => sab);
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => new NzbHandler());
        });
        var first = await SeedAsync(database, pending: true);
        var library = Path.Combine(database.Location.DataDirectory, "library");
        Directory.CreateDirectory(library);
        var source = Path.Combine(library, "existing.mkv");
        await File.WriteAllBytesAsync(source, new byte[200_000], token);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var now = database.Time.GetUtcNow();
            await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.OnboardingStep, OnboardingStep.Complete)
                .SetProperty(row => row.PrdbUserHash, "account").SetProperty(row => row.PrdbApiKey, "fixture")
                .SetProperty(row => row.LibraryRoot, library).SetProperty(row => row.SabnzbdUrl, "http://sabnzbd.invalid")
                .SetProperty(row => row.SabnzbdApiKey, "fixture").SetProperty(row => row.SabnzbdCategory, "xxx"), token);
            context.FeedCursors.Add(new() { Feed = Feed.WantedVideos, Cursor = FeedPosition.CaughtUpAt(now).Stored });
            await context.CatalogueVideos.ExecuteUpdateAsync(update => update.SetProperty(row => row.ReleaseDate, DateOnly.FromDateTime(now.DateTime))
                .SetProperty(row => row.LastReadAt, now), token);
            for (var i = 0; i < 110; i++) await AddRecentAsync(context, first.IndexerId, now, now.AddDays(-2), i);
            await context.SaveChangesAsync(token);
            Assert.True((await scope.ServiceProvider.GetRequiredService<AutopilotSettings>().StartAsync(true, [], token)).Accepted);
            var direct = await scope.ServiceProvider.GetRequiredService<PersonDownloads>().AutomaticDownloadAsync(Guid.NewGuid(), first.VideoId,
                (await context.Releases.OrderBy(row => row.Id).FirstAsync(token)).Id, token);
            Assert.NotEqual(DownloadOutcome.Submitted, direct?.Outcome);
        }
        for (var i = 0; i < 3; i++)
        {
            await using var scope = database.Scope();
            await scope.ServiceProvider.GetRequiredService<CollectionScanRoutine>().RunAsync(null, token);
            await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>().RunAsync(null, token);
        }
        Assert.DoesNotContain("addfile", sab.Modes);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var arrival = await context.ArrivingFiles.SingleAsync(token);
            api.Answers("/videos/identify", System.Text.Json.JsonSerializer.Serialize(new { results = new[] { new {
                @ref = arrival.Id.ToString("D"), videoId = first.VideoId, confidence = 4, matchedBy = 0,
                video = new { id = first.VideoId, title = "Existing scene", site = new { id = Guid.NewGuid(), title = "Site" } } } } }));
            await scope.ServiceProvider.GetRequiredService<ArrivalIdentificationRoutine>().RunAsync(null, token);
        }
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<CollectionScanRoutine>().RunAsync(null, token);
            await scope.ServiceProvider.GetRequiredService<CollectionPlanningRoutine>().RunAsync(null, token);
            var preview = scope.ServiceProvider.GetRequiredService<CollectionPreview>();
            var page = await preview.ReadAsync(1, token);
            Assert.Equal("Preview", page.Phase);
            await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>().RunAsync(null, token);
            Assert.DoesNotContain("addfile", sab.Modes);
            Assert.True((await preview.ApproveAsync(page.Revision, [], [], true, false, token)).Accepted);
        }
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<CollectionImportRoutine>().RunAsync(null, token);
            await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>().RunAsync(null, token);
            Assert.DoesNotContain("addfile", sab.Modes);
            await scope.ServiceProvider.GetRequiredService<FilingRoutine>().RunAsync(null, token);
        }
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<CollectionImportRoutine>().RunAsync(null, token);
            await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().ReconcileAsync(token);
            Assert.Equal(100, await scope.ServiceProvider.GetRequiredService<FabDbContext>().AutopilotMemberships.CountAsync(row => row.Admitted, token));
        }
        for (var batch = 0; batch < 5; batch++)
        {
            for (var turn = 0; turn < 6; turn++)
            {
                await using var scope = database.Scope();
                await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>().RunAsync(null, token);
            }
            await using var complete = database.Scope();
            var context = complete.ServiceProvider.GetRequiredService<FabDbContext>();
            var active = await context.Downloads.AsTracking().Where(row => row.State == DownloadState.Outstanding).ToListAsync(token);
            Assert.Equal(20, active.Count);
            foreach (var download in active)
            {
                download.State = DownloadState.Collected;
                context.LibraryEntries.Add(new() { VideoId = download.VideoId, EntryDirectory = Path.Combine(library, download.VideoId.ToString()) });
            }
            await context.SaveChangesAsync(token);
            await complete.ServiceProvider.GetRequiredService<WantedAutopilot>().ReconcileAsync(token);
        }
        Assert.Equal(100, sab.Modes.Count(mode => mode == "addfile"));
        database.Time.Advance(TimeSpan.FromMinutes(10));
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await AddRecentAsync(context, first.IndexerId, database.Time.GetUtcNow(), database.Time.GetUtcNow(), 999);
            await context.SaveChangesAsync(token);
            await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().ReconcileAsync(token);
        }
        for (var i = 0; i < 8; i++)
        {
            await using var scope = database.Scope();
            await scope.ServiceProvider.GetRequiredService<AutomaticDecisionRoutine>().RunAsync(null, token);
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().ReconcileAsync(token));
        }
        Assert.Equal(101, sab.Modes.Count(mode => mode == "addfile"));
        Assert.False(File.Exists(source));
    }

    private static async Task AddRecentAsync(FabDbContext context, Guid indexer, DateTimeOffset now, DateTimeOffset since, int number)
    {
        var token = TestContext.Current.CancellationToken;
        var video = new CatalogueVideoRow { PrdbId = Guid.NewGuid(), Title = "Recent scene " + number,
            ReleaseDate = DateOnly.FromDateTime(now.DateTime).AddDays(-(number % 60)), LastReadAt = now };
        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(token);
        context.WantedVideos.Add(new() { VideoId = video.Id, SinceAt = since });
        context.Releases.Add(new() { IndexerId = indexer, DerivedReleaseId = "release-" + number, RawGuid = "release-" + number,
            Title = "Recent scene " + number, Size = 1000, Categories = "[]", PostDate = now, PubDate = now, FirstSeenAt = now,
            DownloadUrl = "https://indexer.invalid/nzb/" + number, IdentificationState = IdentificationState.Matched,
            LastIdentifiedAt = now, VideoId = video.Id, Confidence = IdentificationConfidence.Exact, MatchedBy = IdentificationRung.ReleaseName, AutomationPending = true });
    }

    private sealed class CollectionProbe : IProbeProcess
    {
        public Task<ProbeProcessResult> RunAsync(string path, CancellationToken cancellationToken) => Task.FromResult(new ProbeProcessResult(0,
            """{"streams":[{"width":1920,"height":1080,"codec_name":"h264"}],"format":{"duration":"60"}}""", "", false));
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
            LastIdentifiedAt = database.Time.GetUtcNow(),
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
                "addfile" => System.Text.Json.JsonSerializer.Serialize(new { status = true, nzo_ids = new[] { Modes.Count(mode => mode == "addfile") == 1 ? "automatic-nzo" : "automatic-nzo-" + Modes.Count } }),
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
