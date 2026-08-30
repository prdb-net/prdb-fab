using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Prdb.Sdk.Generated.Models;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.ReleaseDiscovery;

public sealed class IdentificationCutTests
{
    private static readonly Guid IndexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000011");
    private static readonly Guid OtherIndexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000012");
    private static readonly Guid MatchedVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000011");
    private static readonly Guid FirstCandidate = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000012");
    private static readonly Guid SecondCandidate = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000013");
    private static readonly Guid IdentifiedSite = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000011");

    [Fact]
    public async Task Forward_screening_is_bounded_and_waits_for_the_relevant_catalogue_detail()
    {
        await using var database = await TestDatabase.CreateAsync();
        var videoId = await SeedWantedAsync(database, "A Wanted Video", lastRead: default);
        await SeedIndexerAsync(database, IndexerId);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.CatalogueVideoPreNames.Add(new CatalogueVideoPreNameRow
            {
                VideoId = videoId,
                PreName = "A.Release.Name",
                NormalisedPreName = ComparisonForm.Of("A.Release.Name"),
            });
            context.Releases.AddRange(Enumerable.Range(1, ScreeningRoutine.BatchSize + 1).Select(index =>
                Release(
                    $"release-{index}",
                    index == 1 ? "A.Release.Name.1080p.mkv" : $"Unrelated.{index}.mkv",
                    IdentificationState.Unexamined,
                    database.Time.GetUtcNow().AddSeconds(index))));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ScreeningRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ItemsHandled);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(
                IdentificationState.Awaiting,
                (await context.Releases.SingleAsync(
                    row => row.DerivedReleaseId == "release-1",
                    TestContext.Current.CancellationToken)).IdentificationState);
            Assert.Equal(
                ScreeningRoutine.BatchSize,
                await context.Releases.CountAsync(
                    row => row.IdentificationState == IdentificationState.Unexamined,
                    TestContext.Current.CancellationToken));

            await context.CatalogueVideos
                .Where(row => row.Id == videoId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.LastReadAt, database.Time.GetUtcNow()),
                    TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ScreeningRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(ScreeningRoutine.BatchSize, result.ItemsHandled);

            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(
                ScreeningRoutine.BatchSize,
                await context.Releases.CountAsync(
                    row => row.IdentificationState == IdentificationState.Unremarkable,
                    TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Backwards_screening_reconsiders_old_answers_and_a_later_needle_cannot_fall_behind()
    {
        await using var database = await TestDatabase.CreateAsync();
        var videoId = await SeedWantedAsync(database, "First Needle", database.Time.GetUtcNow());
        await SeedIndexerAsync(database, IndexerId);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(
                Release("unremarkable", "First.Needle.1080p.mkv", IdentificationState.Unremarkable, database.Time.GetUtcNow()),
                Release("unknown", "First.Needle.720p.mkv", IdentificationState.Unknown, database.Time.GetUtcNow()),
                Release("site-only", "First.Needle.2160p.mkv", IdentificationState.SiteOnly, database.Time.GetUtcNow()));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunBackwardsAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(
                3,
                await context.Releases.CountAsync(
                    row => row.IdentificationState == IdentificationState.Awaiting,
                    TestContext.Current.CancellationToken));
            Assert.True((await context.CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken)).TitleSearchedBackwards);

            context.CatalogueVideoPreNames.Add(new CatalogueVideoPreNameRow
            {
                VideoId = videoId,
                PreName = "Later Needle",
                NormalisedPreName = ComparisonForm.Of("Later Needle"),
            });
            context.Releases.Add(Release(
                "later",
                "Later.Needle.1080p.mkv",
                IdentificationState.Unknown,
                database.Time.GetUtcNow()));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunBackwardsAsync(database);

        await using var check = database.Scope();
        var checkedContext = check.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(
            IdentificationState.Awaiting,
            (await checkedContext.Releases.SingleAsync(
                row => row.DerivedReleaseId == "later",
                TestContext.Current.CancellationToken)).IdentificationState);
        Assert.True((await checkedContext.CatalogueVideoPreNames.SingleAsync(TestContext.Current.CancellationToken)).SearchedBackwards);
    }

    [Fact]
    public async Task Prdbs_four_final_answers_are_stored_as_named_states_and_keep_candidate_videos_pinned()
    {
        var prdb = new FakePrdbApi()
            .Answers("/videos/identify", IdentifyAnswer())
            .Answers("/videos/batch", VideoDetailsAnswer());
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(
                Release("matched", "Matched.Release.mkv", IdentificationState.Awaiting, database.Time.GetUtcNow(), searchWasReason: true),
                Release("ambiguous", "Ambiguous.Release.mkv", IdentificationState.Awaiting, database.Time.GetUtcNow()),
                Release("site", "Site.Release.mkv", IdentificationState.Awaiting, database.Time.GetUtcNow()),
                Release("unknown", "Unknown.Release.mkv", IdentificationState.Awaiting, database.Time.GetUtcNow()));
            context.IdentificationOutcomes.Add(new IdentificationOutcomeRow
            {
                At = database.Time.GetUtcNow().AddDays(-8),
                Gate = "BeforeDownload",
                Outcome = "Old",
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(4, result.ItemsHandled);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var rows = await context.Releases.ToDictionaryAsync(
                row => row.DerivedReleaseId,
                TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationState.Matched, rows["matched"].IdentificationState);
            Assert.Equal(IdentificationConfidence.Exact, rows["matched"].Confidence);
            Assert.Equal(IdentificationRung.ReleaseName, rows["matched"].MatchedBy);
            Assert.False(rows["matched"].SearchWasReason);
            Assert.Equal(IdentificationState.Ambiguous, rows["ambiguous"].IdentificationState);
            Assert.Equal(IdentificationState.SiteOnly, rows["site"].IdentificationState);
            Assert.Equal(IdentificationState.Unknown, rows["unknown"].IdentificationState);
            Assert.Equal(2, await context.ReleaseCandidates.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(
                ["Ambiguous", "Exact", "SiteOnly", "Unknown"],
                await context.IdentificationOutcomes
                    .OrderBy(row => row.Outcome)
                    .Select(row => row.Outcome)
                    .ToArrayAsync(TestContext.Current.CancellationToken));

            var candidate = await context.CatalogueVideos.SingleAsync(
                row => row.PrdbId == FirstCandidate,
                TestContext.Current.CancellationToken);
            var pins = scope.ServiceProvider.GetRequiredService<CataloguePins>();
            Assert.True(await pins.IsPinnedAsync(candidate.Id, TestContext.Current.CancellationToken));
            Assert.Contains(
                FirstCandidate,
                await pins.NewestPinFirst(context.CatalogueVideos)
                    .Select(row => row.PrdbId)
                    .ToListAsync(TestContext.Current.CancellationToken));
        }

        var request = Assert.Single(prdb.AskingFor("/videos/identify"));
        using var body = JsonDocument.Parse(request.Body);
        var files = body.RootElement.GetProperty("files");
        Assert.Equal(4, files.GetArrayLength());
        Assert.Equal("Matched.Release.mkv", files[0].GetProperty("filename").GetString());
        Assert.True(IsNullOrMissing(files[0], "filesize"));
        Assert.True(IsNullOrMissing(files[0], "osHash"));
        Assert.True(IsNullOrMissing(files[0], "pHash"));
        Assert.False(body.RootElement.GetProperty("includeVideoDetails").GetBoolean());
    }

    [Fact]
    public async Task A_recent_settled_release_is_identified_again_when_due_but_not_before()
    {
        var prdb = new FakePrdbApi()
            .Answers("/videos/identify", $$"""
                {
                  "results": [
                    { "ref": "1", "videoId": "{{MatchedVideo}}", "confidence": 4, "matchedBy": 3, "candidates": [] }
                  ]
                }
                """)
            .Answers("/videos/batch", VideoDetailsAnswer());
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await SeedIndexerAsync(database, IndexerId);
        await SetPrdbKeyAsync(database);
        var now = database.Time.GetUtcNow();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var due = Release("due", "Due.Release.mkv", IdentificationState.Unknown, now.AddDays(-1));
            due.LastIdentifiedAt = now - RecentWindow.RevalidateAfter;
            var fresh = Release("fresh", "Fresh.Release.mkv", IdentificationState.Unknown, now.AddDays(-1));
            fresh.LastIdentifiedAt = now;
            context.Releases.AddRange(due, fresh);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ItemsHandled);
        }

        await using var check = database.Scope();
        var rows = await check.ServiceProvider.GetRequiredService<FabDbContext>().Releases
            .ToDictionaryAsync(row => row.DerivedReleaseId, TestContext.Current.CancellationToken);
        Assert.Equal(IdentificationState.Matched, rows["due"].IdentificationState);
        Assert.Equal(now, rows["due"].LastIdentifiedAt);
        Assert.Equal(IdentificationState.Unknown, rows["fresh"].IdentificationState);
        Assert.Equal(now, rows["fresh"].LastIdentifiedAt);
    }

    [Fact]
    public async Task Eviction_is_oldest_first_and_never_touches_unexamined_or_still_wanted_releases()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database, IndexerId);
        var wantedVideo = await SeedWantedAsync(database, "Wanted Video", database.Time.GetUtcNow());
        var now = database.Time.GetUtcNow();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(
                Release("unexamined", "One", IdentificationState.Unexamined, now.AddMinutes(-6)),
                Release("wanted", "Two", IdentificationState.Matched, now.AddMinutes(-5), videoId: wantedVideo),
                Release("old-disposable", "Three", IdentificationState.Unremarkable, now.AddMinutes(-4)),
                Release("middle-disposable", "Four", IdentificationState.Unknown, now.AddMinutes(-3)),
                Release("new-disposable", "Five", IdentificationState.SiteOnly, now.AddMinutes(-2)),
                Release("newest-disposable", "Six", IdentificationState.Unremarkable, now.AddMinutes(-1)),
                Release("recent-disposable", "Seven", IdentificationState.Unremarkable, now));
            foreach (var release in context.ChangeTracker.Entries<ReleaseRow>().Select(entry => entry.Entity))
            {
                release.PostDate = now.AddDays(-RecentWindow.Days - 1);
            }
            context.ChangeTracker.Entries<ReleaseRow>()
                .Single(entry => entry.Entity.DerivedReleaseId == "recent-disposable")
                .Entity.PostDate = RecentWindow.BeginsAt(now);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ReleaseEviction>()
                .EvictAsync(IndexerId, ceiling: 3, TestContext.Current.CancellationToken);
            Assert.Equal(4, result.Removed);
            Assert.Equal(0, result.OverBy);
        }

        await using var check = database.Scope();
        var left = await check.ServiceProvider.GetRequiredService<FabDbContext>().Releases
            .OrderBy(row => row.FirstSeenAt)
            .Select(row => row.DerivedReleaseId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["unexamined", "wanted", "recent-disposable"], left);
    }

    [Fact]
    public async Task An_unexamined_backlog_over_the_ceiling_is_reported_and_loses_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database, IndexerId);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Releases.AddRange(Enumerable.Range(1, 4).Select(index =>
                Release($"held-{index}", $"Held {index}", IdentificationState.Unexamined, database.Time.GetUtcNow())));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var result = await scope.ServiceProvider.GetRequiredService<ReleaseEviction>()
                .EvictAsync(IndexerId, ceiling: 2, TestContext.Current.CancellationToken);
            Assert.Equal(0, result.Removed);
            Assert.Equal(2, result.OverBy);
            Assert.Equal(4, await context.Releases.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task A_wanted_sweep_uses_the_title_once_and_a_repeat_does_not_reopen_a_settled_release()
    {
        var indexer = new EmptyIndexer(Feed("stable-release", "A.Long.Title.1080p.mkv"));
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => indexer));
        await SeedIndexerAsync(database, IndexerId, dailyBudget: 1000, withWalkState: true);
        await SeedWantedAsync(database, "A Long Title", database.Time.GetUtcNow());

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<WantedSweepRoutine>()
                .RunAsync(IndexerId.ToString("D"), TestContext.Current.CancellationToken);
            Assert.Equal(1, result.RowsAdded);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Releases.ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.IdentificationState, IdentificationState.Unknown)
                    .SetProperty(row => row.SearchWasReason, false),
                TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<WantedSweepRoutine>()
                .RunAsync(IndexerId.ToString("D"), TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var release = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Releases
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(IdentificationState.Unknown, release.IdentificationState);
            Assert.False(release.SearchWasReason);
        }

        Assert.Equal(2, indexer.Requests.Count);
        Assert.All(indexer.Requests, request =>
        {
            var query = HttpUtility.ParseQueryString(request.Query);
            Assert.Equal("A Long Title", query["q"]);
            Assert.Equal("0", query["offset"]);
            Assert.Null(query["maxage"]);
        });
    }

    [Fact]
    public async Task A_fruitless_wanted_sweep_does_not_demote_the_video()
    {
        var indexer = new EmptyIndexer("<?xml version=\"1.0\"?><rss><channel /></rss>");
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => indexer));
        await SeedIndexerAsync(database, IndexerId, withWalkState: true);
        await SeedWantedAsync(database, "A Long Missing Title", database.Time.GetUtcNow());

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<WantedSweepRoutine>()
                .RunAsync(IndexerId.ToString("D"), TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<FabDbContext>()
            .WantedVideoSweepStates.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Query_budgets_are_per_indexer_and_the_walk_cannot_spend_the_sweep_share()
    {
        var indexer = new EmptyIndexer("<?xml version=\"1.0\"?><rss><channel /></rss>");
        await using var database = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Indexers).ConfigurePrimaryHttpMessageHandler(() => indexer));
        await SeedIndexerAsync(database, IndexerId, dailyBudget: 2, withWalkState: true);
        await SeedIndexerAsync(database, OtherIndexerId, dailyBudget: 2, withWalkState: true);
        await SeedWantedAsync(database, "A Long Title", database.Time.GetUtcNow());

        await using var scope = database.Scope();
        var search = scope.ServiceProvider.GetRequiredService<IndexerSearch>();

        Assert.NotNull((await search.PageAsync(
            IndexerId, 0, null, IndexerQueryPurpose.Walk, null,
            TestContext.Current.CancellationToken)).Read);
        Assert.NotNull((await search.PageAsync(
            IndexerId, 0, null, IndexerQueryPurpose.Walk, null,
            TestContext.Current.CancellationToken)).DeferredFor);
        Assert.NotNull((await search.PageAsync(
            IndexerId, 0, null, IndexerQueryPurpose.WantedSweep, "A Long Title",
            TestContext.Current.CancellationToken)).Read);
        Assert.NotNull((await search.PageAsync(
            OtherIndexerId, 0, null, IndexerQueryPurpose.Walk, null,
            TestContext.Current.CancellationToken)).Read);
    }

    [Fact]
    public async Task A_catalogue_title_change_makes_every_indexer_pair_due_again()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedIndexerAsync(database, IndexerId);
        var videoId = await SeedWantedAsync(database, "Old Long Title", database.Time.GetUtcNow());

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.WantedVideoSweepStates.Add(new WantedVideoSweepStateRow
            {
                VideoId = videoId,
                IndexerId = IndexerId,
                LastSearchedAt = database.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var video = await context.CatalogueVideos.SingleAsync(
                row => row.Id == videoId,
                TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<VideoDetails>().WriteAsync(
                new VideoDetailDto { Id = video.PrdbId, Title = "New Long Title" },
                TestContext.Current.CancellationToken);
        }

        await using var check = database.Scope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<FabDbContext>()
            .WantedVideoSweepStates.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static bool IsNullOrMissing(JsonElement element, string property) =>
        !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

    private static async Task RunBackwardsAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<BackwardsScreeningRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static async Task<long> SeedWantedAsync(
        TestDatabase database,
        string title,
        DateTimeOffset lastRead)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var video = new CatalogueVideoRow
        {
            PrdbId = Guid.NewGuid(),
            Title = title,
            NormalisedTitle = ComparisonForm.Of(title),
            LastReadAt = lastRead,
        };
        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.WantedVideos.Add(new WantedVideoRow
        {
            VideoId = video.Id,
            SinceAt = database.Time.GetUtcNow(),
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return video.Id;
    }

    private static async Task SeedIndexerAsync(
        TestDatabase database,
        Guid id,
        int dailyBudget = 1000,
        bool withWalkState = false)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Indexers.Add(new IndexerRow
        {
            Id = id,
            Name = id == IndexerId ? "First" : "Second",
            Url = $"https://{id:N}.indexer.invalid/api",
            ApiKey = "indexer-key",
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = database.Time.GetUtcNow(),
            DailyQueryBudget = dailyBudget,
        });

        if (withWalkState)
        {
            context.IndexerWalkStates.Add(new IndexerWalkStateRow
            {
                IndexerId = id,
                CapsTree = "[]",
                ResolvedCategoryIds = "[]",
                MissingCategoryNames = "[]",
                QueryDay = new DateTimeOffset(database.Time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero),
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SetPrdbKeyAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.PrdbApiKey, "prdb-key"),
            TestContext.Current.CancellationToken);
    }

    private static ReleaseRow Release(
        string id,
        string title,
        IdentificationState state,
        DateTimeOffset firstSeen,
        bool searchWasReason = false,
        long? videoId = null) => new()
    {
        IndexerId = IndexerId,
        DerivedReleaseId = id,
        RawGuid = id,
        Title = title,
        NormalisedTitle = ComparisonForm.Of(title),
        Categories = "[]",
        PostDate = firstSeen,
        PubDate = firstSeen,
        FirstSeenAt = firstSeen,
        IdentificationState = state,
        SearchWasReason = searchWasReason,
        VideoId = videoId,
    };

    private static string IdentifyAnswer() => $$"""
        {
          "results": [
            { "ref": "1", "videoId": "{{MatchedVideo}}", "confidence": 4, "matchedBy": 3, "candidates": [] },
            { "ref": "2", "confidence": 5, "candidates": ["{{FirstCandidate}}", "{{SecondCandidate}}"] },
            { "ref": "3", "confidence": 1, "matchedBy": 4, "site": { "id": "{{IdentifiedSite}}", "title": "A Site" }, "candidates": [] },
            { "ref": "4", "confidence": 0, "candidates": [] }
          ]
        }
        """;

    private static string VideoDetailsAnswer() => $$"""
        [
          { "id": "{{MatchedVideo}}", "title": "Matched Video", "preNames": [], "actors": [], "images": [] },
          { "id": "{{FirstCandidate}}", "title": "First Candidate", "preNames": [], "actors": [], "images": [] },
          { "id": "{{SecondCandidate}}", "title": "Second Candidate", "preNames": [], "actors": [], "images": [] }
        ]
        """;

    private static string Feed(string id, string title)
    {
        var date = new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
        return $$"""
            <?xml version="1.0"?>
            <rss xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
              <channel>
                <item>
                  <title>{{title}}</title>
                  <guid>https://indexer.invalid/details/{{id}}</guid>
                  <link>https://indexer.invalid/get/{{id}}</link>
                  <pubDate>{{date.ToString("R", CultureInfo.InvariantCulture)}}</pubDate>
                  <newznab:attr name="usenetdate" value="{{date:O}}" />
                </item>
              </channel>
            </rss>
            """;
    }

    private sealed class EmptyIndexer(string response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/xml"),
            });
        }
    }
}
