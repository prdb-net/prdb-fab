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
        }
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

    private static DownloadRow Download(TestDatabase database, Guid videoId, Guid indexerId, string identity) => new()
    {
        Id = Guid.CreateVersion7(database.Time.GetUtcNow()),
        VideoId = videoId,
        IndexerId = indexerId,
        DerivedReleaseId = identity,
        SubmittedName = identity,
        State = DownloadState.Outstanding,
        OutstandingSince = database.Time.GetUtcNow(),
        OriginIsPerson = true,
        CreatedAt = database.Time.GetUtcNow(),
    };

    private sealed record Seeded(Guid VideoId, long LocalVideoId, Guid IndexerId);

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
}
