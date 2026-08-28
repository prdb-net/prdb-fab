using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Acquisition;

public sealed class AcquisitionTests
{
    [Fact]
    public async Task The_release_discovery_schema_migrates_cleanly_to_acquisition()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: "ReleaseDiscoveryIdentification");

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>()
                .PrepareAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(3, (await context.Installation.SingleAsync(TestContext.Current.CancellationToken)).RetryBudget);
            Assert.Equal(0, await context.Downloads.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.ReleasesNotDownloaded.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_real_ranking_decision_remembers_exclusions_and_prunes_the_old_window()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(
                Release(seeded, "eligible", 1000, IdentificationConfidence.Exact),
                Release(seeded, "password", 2000, IdentificationConfidence.Exact, password: "1"),
                Release(seeded, "partial", 3000, IdentificationConfidence.Partial),
                Release(seeded, "consumed", 4000, IdentificationConfidence.Strong));
            context.ReleasesNotDownloaded.Add(new ReleaseNotDownloadedRow
            {
                At = database.Time.GetUtcNow().AddDays(-8),
                Reason = "old",
            });
            context.Downloads.Add(Download(database, seeded.VideoId, seeded.IndexerId, "consumed"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var ranking = await scope.ServiceProvider.GetRequiredService<ReleaseRankings>()
                .ForVideoAsync(seeded.VideoId, observeDecision: true, TestContext.Current.CancellationToken);
            Assert.Equal("eligible", Assert.Single(ranking!.Ranked).DerivedReleaseId);
            Assert.Equal(
                [ReleaseExclusion.PasswordProtected, ReleaseExclusion.ConfidenceNotAllowed, ReleaseExclusion.Consumed],
                ranking.Excluded.OrderBy(item => item.Id).Select(item => item.Exclusion));
        }

        await using (var scope = database.Scope())
        {
            var reasons = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .ReleasesNotDownloaded.OrderBy(row => row.Reason)
                .Select(row => row.Reason)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["ConfidenceNotAllowed", "Consumed", "PasswordProtected"], reasons);
        }
    }

    [Fact]
    public async Task A_spent_budget_and_no_releases_left_are_distinct_plans()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database);
        long releaseId;

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var release = Release(seeded, "chosen", 1000, IdentificationConfidence.Exact);
            context.Releases.Add(release);
            context.Downloads.AddRange(Enumerable.Range(1, 3).Select(number =>
                Download(database, seeded.VideoId, seeded.IndexerId, $"spent-{number}")));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            releaseId = release.Id;
        }

        await using (var scope = database.Scope())
        {
            var preview = await scope.ServiceProvider.GetRequiredService<PersonDownloads>()
                .PreviewAsync(seeded.VideoId, releaseId, TestContext.Current.CancellationToken);
            Assert.Equal(DownloadPlanOutcome.RetryBudgetSpent, preview!.Outcome);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Downloads.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            context.Downloads.Add(Download(database, seeded.VideoId, seeded.IndexerId, "chosen"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var preview = await scope.ServiceProvider.GetRequiredService<PersonDownloads>()
                .PreviewAsync(seeded.VideoId, releaseId, TestContext.Current.CancellationToken);
            Assert.Equal(DownloadPlanOutcome.NoReleasesLeft, preview!.Outcome);
        }
    }

    [Fact]
    public async Task The_live_sabnzbd_routine_has_a_row_and_records_what_the_check_saw()
    {
        var sabnzbd = new CategoriesHandler();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Sabnzbd).ConfigurePrimaryHttpMessageHandler(() => sabnzbd));

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.SabnzbdUrl, "http://sabnzbd.invalid")
                    .SetProperty(row => row.SabnzbdApiKey, "fixture"),
                TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<RoutineRegistrar>()
                .EnsureRowsExistAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<RoutineRunner>()
                .TurnAsync(Lane.Live, TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var row = await context.Routines.SingleAsync(
                routine => routine.Name == SabnzbdRoutine.RoutineName,
                TestContext.Current.CancellationToken);
            var run = await context.RoutineRuns.SingleAsync(
                item => item.RoutineId == row.Id,
                TestContext.Current.CancellationToken);
            Assert.Equal(Lane.Live, row.Lane);
            Assert.Equal(RunOutcome.Succeeded, run.Outcome);
            Assert.Equal(2, run.ResultsSeen);
            Assert.Equal(0, run.RowsAdded);
            Assert.Equal(["get_cats"], sabnzbd.Modes);
            Assert.True(await context.Routines.AnyAsync(
                routine => routine.Name == DownloadFollowingRoutine.RoutineName
                    && routine.Lane == Lane.Live,
                TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task The_five_minute_reachability_check_is_idle_while_following_is_active()
    {
        var sabnzbd = new CategoriesHandler();
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Sabnzbd).ConfigurePrimaryHttpMessageHandler(() => sabnzbd));
        var seeded = await SeedAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(update => update
            .SetProperty(row => row.SabnzbdUrl, "http://sabnzbd.invalid")
            .SetProperty(row => row.SabnzbdApiKey, "fixture"), TestContext.Current.CancellationToken);
        context.Downloads.Add(Download(database, seeded.VideoId, seeded.IndexerId, "active", "nzo-active"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await scope.ServiceProvider.GetRequiredService<SabnzbdRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(result.Outcome);
        Assert.Empty(sabnzbd.Modes);
    }

    [Fact]
    public async Task Following_reads_queue_then_only_missing_history_and_applies_machine_states()
    {
        var sabnzbd = new FollowingHandler
        {
            Queue = """
                {"queue":{"paused":false,"slots":[
                  {"nzo_id":"unusable","filename":"Encrypted","status":"Paused","labels":["ENCRYPTED"]}
                ]}}
                """,
            History = """
                {"history":{"slots":[
                  {"nzo_id":"complete","name":"Complete","status":"Completed","fail_message":"","stage_log":[],"storage":"/remote/Complete"},
                  {"nzo_id":"failed","name":"Failed","status":"Failed","fail_message":"translated words","stage_log":[{"name":"Unpack","actions":["bad"]}]}
                ]}}
                """,
        };
        await using var database = await FollowingDatabaseAsync(sabnzbd);
        var seeded = await SeedAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Downloads.AddRange(
                Download(database, seeded.VideoId, seeded.IndexerId, "Encrypted", "unusable"),
                Download(database, seeded.VideoId, seeded.IndexerId, "Complete", "complete"),
                Download(database, seeded.VideoId, seeded.IndexerId, "Failed", "failed"),
                Download(database, seeded.VideoId, seeded.IndexerId, "Gone", "gone"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var observed = await scope.ServiceProvider.GetRequiredService<SabnzbdGateway>()
                .ObserveAsync(
                    "http://sabnzbd.invalid",
                    "fixture",
                    ["unusable", "complete", "failed", "gone"],
                    recoverSubmittedNames: false,
                    TestContext.Current.CancellationToken);
            Assert.Equal(SabnzbdConnectionOutcome.Saved, observed.Outcome);
            var observedQueue = Assert.Single(observed.Queue);
            Assert.Equal("unusable", observedQueue.NzoId);
            Assert.Equal("Paused", observedQueue.Status);
            Assert.Equal(["ENCRYPTED"], observedQueue.Labels);
            Assert.Equal(2, observed.History.Count);
            sabnzbd.Modes.Clear();

            var result = await scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        }

        await using (var scope = database.Scope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
                .OrderBy(row => row.SubmittedName)
                .ToListAsync(TestContext.Current.CancellationToken);
            var unusable = rows.Single(row => row.SubmittedName == "Encrypted");
            Assert.True(
                unusable.Cause == DownloadCause.Unusable,
                $"state={unusable.State}; cause={unusable.Cause}; nzo={unusable.NzoId}; status={unusable.LastSabnzbdStatus}; absences={unusable.ConsecutiveAbsences}");
            var complete = rows.Single(row => row.SubmittedName == "Complete");
            Assert.Equal(DownloadState.Completed, complete.State);
            Assert.Equal("/remote/Complete", complete.Storage);
            var failed = rows.Single(row => row.SubmittedName == "Failed");
            Assert.Equal(DownloadCause.Failed, failed.Cause);
            Assert.Equal("translated words", failed.FailMessage);
            Assert.Contains("Unpack", failed.StageLog);
            Assert.Equal(1, rows.Single(row => row.SubmittedName == "Gone").ConsecutiveAbsences);
        }

        Assert.Equal(["queue", "history"], sabnzbd.Modes);
        Assert.Contains("unusable", sabnzbd.LastQueueIds);
        Assert.DoesNotContain("unusable", sabnzbd.LastHistoryIds);
        Assert.Contains("complete", sabnzbd.LastHistoryIds);
    }

    [Fact]
    public async Task A_failed_history_request_and_a_paused_installation_change_no_download_evidence()
    {
        var sabnzbd = new FollowingHandler { HistoryThrows = true };
        await using var database = await FollowingDatabaseAsync(sabnzbd);
        var seeded = await SeedAsync(database);
        Guid id;

        await using (var scope = database.Scope())
        {
            var row = Download(database, seeded.VideoId, seeded.IndexerId, "Unknown", "unknown");
            row.ConsecutiveAbsences = 2;
            row.LastSabnzbdStatus = "Downloading";
            scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads.Add(row);
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
            id = row.Id;

            var result = await scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(RunOutcome.Failed, result.Outcome);
        }

        sabnzbd.HistoryThrows = false;
        sabnzbd.Queue = """{"queue":{"paused":true,"slots":[]}}""";
        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(RunOutcome.Failed, result.Outcome);
        }

        await using (var scope = database.Scope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
                .SingleAsync(download => download.Id == id, TestContext.Current.CancellationToken);
            Assert.Equal(2, row.ConsecutiveAbsences);
            Assert.Equal("Downloading", row.LastSabnzbdStatus);
            Assert.Equal(DownloadState.Outstanding, row.State);
        }
    }

    [Fact]
    public async Task An_uncertain_submission_is_recovered_only_by_one_exact_name()
    {
        var sabnzbd = new FollowingHandler
        {
            Queue = """
                {"queue":{"paused":false,"slots":[
                  {"nzo_id":"recovered","filename":"Exact.Name","status":"Downloading","labels":[]},
                  {"nzo_id":"other","filename":"Exact.Name.extra","status":"Downloading","labels":[]}
                ]}}
                """,
        };
        await using var database = await FollowingDatabaseAsync(sabnzbd);
        var seeded = await SeedAsync(database);

        await using (var scope = database.Scope())
        {
            scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads.Add(
                Download(database, seeded.VideoId, seeded.IndexerId, "Exact.Name", nzoId: null));
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
        }

        await using var read = database.Scope();
        var stored = await read.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("recovered", stored.NzoId);
        Assert.Equal("Downloading", stored.LastSabnzbdStatus);
        Assert.Equal(["queue", "history"], sabnzbd.Modes);
    }

    [Fact]
    public async Task Three_successful_absences_consume_the_release_and_submit_the_next_ranked_one()
    {
        var sabnzbd = new FollowingHandler();
        var indexer = new NzbHandler();
        await using var database = await FollowingDatabaseAsync(sabnzbd, indexer);
        var seeded = await SeedAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.SabnzbdCategory, "xxx"), TestContext.Current.CancellationToken);
            context.Releases.AddRange(
                Release(seeded, "first", 1000, IdentificationConfidence.Exact),
                Release(seeded, "next", 2000, IdentificationConfidence.Exact));
            context.Downloads.Add(Download(database, seeded.VideoId, seeded.IndexerId, "first", "old"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var routine = scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>();
            await routine.RunAsync(null, TestContext.Current.CancellationToken);
            await routine.RunAsync(null, TestContext.Current.CancellationToken);
            await routine.RunAsync(null, TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
                .OrderBy(row => row.CreatedAt)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, rows.Count);
            Assert.Equal(DownloadCause.Vanished, rows[0].Cause);
            Assert.Equal("next", rows[1].DerivedReleaseId);
            Assert.Equal(DownloadState.Outstanding, rows[1].State);
            Assert.Equal("submitted-next", rows[1].NzoId);
        }

        Assert.Equal(1, indexer.Requests);
        Assert.Equal(1, sabnzbd.Modes.Count(mode => mode == "addfile"));
        Assert.DoesNotContain("retry", sabnzbd.Modes);
        Assert.DoesNotContain("delete", sabnzbd.Modes);
    }

    [Fact]
    public async Task Every_terminal_failure_cause_spends_one_attempt_and_uses_the_same_retry_path()
    {
        var sabnzbd = new FollowingHandler();
        var indexerTransport = new NzbHandler();
        await using var database = await FollowingDatabaseAsync(sabnzbd, indexerTransport);
        var indexerId = Guid.NewGuid();
        var causes = Enum.GetValues<DownloadCause>();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.SabnzbdCategory, "xxx"), TestContext.Current.CancellationToken);
            context.Indexers.Add(new IndexerRow
            {
                Id = indexerId,
                Name = "Fixture",
                Url = "https://indexer.invalid/api",
                ApiKey = "fixture",
                LastVerdict = IndexerConnectionOutcome.Saved,
            });

            foreach (var (cause, position) in causes.Select((cause, position) => (cause, position)))
            {
                var video = new CatalogueVideoRow
                {
                    PrdbId = Guid.NewGuid(),
                    Title = $"Video {cause}",
                    NormalisedTitle = $"video {position}",
                    CreatedAtUtc = database.Time.GetUtcNow(),
                    UpdatedAtUtc = database.Time.GetUtcNow(),
                };
                context.CatalogueVideos.Add(video);
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                context.Releases.Add(new ReleaseRow
                {
                    IndexerId = indexerId,
                    DerivedReleaseId = $"next-{position}",
                    RawGuid = $"next-{position}",
                    Title = $"next-{position}",
                    NormalisedTitle = $"next-{position}",
                    Categories = "[]",
                    DownloadUrl = "https://indexer.invalid/nzb",
                    FirstSeenAt = database.Time.GetUtcNow(),
                    PostDate = database.Time.GetUtcNow(),
                    PubDate = database.Time.GetUtcNow(),
                    IdentificationState = IdentificationState.Matched,
                    VideoId = video.Id,
                    Confidence = IdentificationConfidence.Exact,
                    MatchedBy = IdentificationRung.ReleaseName,
                });
                var failed = Download(database, video.PrdbId, indexerId, $"spent-{position}", $"old-{position}");
                failed.State = DownloadState.Failed;
                failed.Cause = cause;
                context.Downloads.Add(failed);
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var result = await scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        }

        await using var read = database.Scope();
        var rows = await read.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(causes.Length * 2, rows.Count);
        Assert.All(rows.GroupBy(row => row.VideoId), group => Assert.Equal(2, group.Count()));
        Assert.All(rows, row => Assert.True(row.OriginIsPerson));
        Assert.Equal(causes.Length, sabnzbd.Modes.Count(mode => mode == "addfile"));
        Assert.Equal(causes.Length, indexerTransport.Requests);
    }

    [Fact]
    public async Task A_download_pins_its_release_in_the_disposable_indexer_cache()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(
                Release(seeded, "downloaded", 1000, IdentificationConfidence.Exact),
                Release(seeded, "disposable", 2000, IdentificationConfidence.Exact));
            context.Downloads.Add(Download(database, seeded.VideoId, seeded.IndexerId, "downloaded"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var result = await scope.ServiceProvider.GetRequiredService<ReleaseEviction>()
                .EvictAsync(seeded.IndexerId, ceiling: 1, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.Removed);
        }

        await using var read = database.Scope();
        Assert.Equal(
            ["downloaded"],
            await read.ServiceProvider.GetRequiredService<FabDbContext>().Releases
                .Select(row => row.DerivedReleaseId)
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Seeded> SeedAsync(TestDatabase database)
    {
        var indexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000041");
        var videoId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000041");
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var indexer = new IndexerRow
        {
            Id = indexerId,
            Name = "Fixture",
            Url = "https://indexer.invalid/api",
            ApiKey = "fixture",
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = database.Time.GetUtcNow(),
            Rank = 2,
        };
        var video = new CatalogueVideoRow
        {
            PrdbId = videoId,
            Title = "A Video",
            NormalisedTitle = "a video",
            CreatedAtUtc = database.Time.GetUtcNow(),
            UpdatedAtUtc = database.Time.GetUtcNow(),
        };
        context.AddRange(indexer, video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(videoId, video.Id, indexerId);
    }

    private static ReleaseRow Release(
        Seeded seeded,
        string identity,
        long size,
        IdentificationConfidence confidence,
        string? password = null) => new()
        {
            IndexerId = seeded.IndexerId,
            DerivedReleaseId = identity,
            RawGuid = identity,
            Title = identity,
            NormalisedTitle = identity,
            Size = size,
            Categories = "[]",
            PostDate = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            PubDate = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            DownloadUrl = "https://indexer.invalid/nzb",
            Password = password,
            FirstSeenAt = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero),
            IdentificationState = IdentificationState.Matched,
            VideoId = seeded.LocalVideoId,
            Confidence = confidence,
            MatchedBy = IdentificationRung.ReleaseName,
        };

    private static DownloadRow Download(
        TestDatabase database,
        Guid videoId,
        Guid indexerId,
        string identity,
        string? nzoId = null) => new()
    {
        Id = Guid.CreateVersion7(database.Time.GetUtcNow()),
        VideoId = videoId,
        IndexerId = indexerId,
        DerivedReleaseId = identity,
        SubmittedName = identity,
        NzoId = nzoId,
        State = DownloadState.Outstanding,
        OutstandingSince = database.Time.GetUtcNow(),
        OriginIsPerson = true,
        CreatedAt = database.Time.GetUtcNow(),
    };

    private sealed record Seeded(Guid VideoId, long LocalVideoId, Guid IndexerId);

    private static async Task<TestDatabase> FollowingDatabaseAsync(
        FollowingHandler sabnzbd,
        HttpMessageHandler? indexer = null)
    {
        var database = await TestDatabase.CreateAsync(also: services =>
        {
            services.AddHttpClient(FabTransports.Sabnzbd).ConfigurePrimaryHttpMessageHandler(() => sabnzbd);
            if (indexer is not null)
            {
                services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => indexer);
            }
        });
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
            update => update
                .SetProperty(row => row.SabnzbdUrl, "http://sabnzbd.invalid")
                .SetProperty(row => row.SabnzbdApiKey, "fixture"),
            TestContext.Current.CancellationToken);
        return database;
    }

    private sealed class CategoriesHandler : HttpMessageHandler
    {
        public List<string> Modes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri?.Query ?? string.Empty);
            Modes.Add(query["mode"] ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"categories\":[\"*\",\"xxx\"]}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FollowingHandler : HttpMessageHandler
    {
        public string Queue { get; set; } = """{"queue":{"paused":false,"slots":[]}}""";
        public string History { get; set; } = """{"history":{"slots":[]}}""";
        public bool HistoryThrows { get; set; }
        public List<string> Modes { get; } = [];
        public string LastQueueIds { get; private set; } = string.Empty;
        public string LastHistoryIds { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri?.Query ?? string.Empty);
            var mode = query["mode"] ?? string.Empty;
            Modes.Add(mode);
            if (mode == "queue")
            {
                LastQueueIds = query["nzo_ids"] ?? string.Empty;
                return Json(Queue);
            }

            if (mode == "history")
            {
                LastHistoryIds = query["nzo_ids"] ?? string.Empty;
                if (HistoryThrows) throw new HttpRequestException("history unavailable");
                return Json(History);
            }

            if (mode == "get_cats") return Json("{\"categories\":[\"xxx\"]}");
            if (mode == "addfile")
            {
                var name = query["nzbname"] ?? string.Empty;
                _ = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return Json($"{{\"status\":true,\"nzo_ids\":[\"submitted-{name}\"]}}");
            }

            return Json("{}");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

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
}
