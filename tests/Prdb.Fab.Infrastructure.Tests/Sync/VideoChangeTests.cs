using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class VideoChangeTests
{
    private static readonly Guid Video = Guid.NewGuid();
    private static readonly Guid Image = Guid.NewGuid();
    private static readonly DateTimeOffset Stamp = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_server_bound_precedes_local_baseline_and_an_idle_feed_replaces_daily_detail_repair()
    {
        var api = new FakePrdbApi().Answers("/videos/changes", Empty(Stamp))
            .Answers("/videos/changes", Empty(Stamp.AddDays(2)))
            .Answers("/videos/batch", $$$"""[{"id":"{{{Video}}}","title":"Current","createdAtUtc":"{{{Stamp:O}}}","updatedAtUtc":"{{{Stamp:O}}}"}]""");
        await using var db = await CreateAsync(api);
        await RunAsync<VideoFeedRoutine>(db);
        await RunAsync<CatalogueRepairRoutine>(db);
        await RunAsync<CatalogueRepairRoutine>(db);
        Assert.Single(api.AskedFor("/videos/batch"));
        db.Time.Advance(TimeSpan.FromDays(2));
        await RunAsync<VideoFeedRoutine>(db);
        await RunAsync<CatalogueRepairRoutine>(db);
        Assert.Single(api.AskedFor("/videos/batch"));
        await using var scope = db.Scope();
        var state = await scope.ServiceProvider.GetRequiredService<FabDbContext>().VideoChangeState.SingleAsync(Token);
        Assert.NotNull(state.BaselineCompletedAt);
        Assert.True(await scope.ServiceProvider.GetRequiredService<VideoChangeCoverage>().FreshAsync(Token));
        Assert.Equal("/videos/changes", api.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Nested_changes_reconcile_an_old_pinned_video_and_clear_removed_artwork_without_a_detail_request()
    {
        var site = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var api = new FakePrdbApi().Answers("/videos/changes", Empty(Stamp))
            .Answers("/videos/changes", $$$"""
            {"items":[{"eventType":"updated","video":{"id":"{{{Video}}}","title":"Corrected","createdAtUtc":"{{{Stamp.AddYears(-1):O}}}","updatedAtUtc":"{{{Stamp.AddMinutes(1):O}}}","durationMs":123000,"description":"From the change feed","stashdbSceneId":"00000000-0000-4000-8000-000000000001","site":{"id":"{{{site}}}","title":"New Site","url":"https://example.test/creator","platformTitle":"Creator Platform","accountHandle":"creator"},"preNames":[{"title":"New needle"}],"actors":[{"id":"{{{actor}}}","name":"Credit"}],"images":[]}}],"hasMore":false,"serverTimeUtc":"{{{Stamp.AddMinutes(2):O}}}","nextCursor":{"id":"{{{Video}}}","updatedAtUtc":"{{{Stamp.AddMinutes(1):O}}}"}}
            """);
        await using var db = await CreateAsync(api);
        await RunAsync<VideoFeedRoutine>(db);
        await RunAsync<VideoFeedRoutine>(db);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var video = await context.CatalogueVideos.SingleAsync(Token);
        Assert.Equal("Corrected", video.Title);
        Assert.False(video.TitleSearchedBackwards);
        Assert.Equal(123000, video.DurationMs);
        Assert.Equal("From the change feed", video.Description);
        Assert.Equal(Guid.Parse("00000000-0000-4000-8000-000000000001"), video.StashdbSceneId);
        Assert.Equal("Creator Platform", (await context.CatalogueSites.SingleAsync(Token)).PlatformTitle);
        Assert.Equal(site, (await context.CatalogueSites.SingleAsync(Token)).PrdbId);
        Assert.Equal("New needle", (await context.CatalogueVideoPreNames.SingleAsync(Token)).PreName);
        Assert.Single(await context.CatalogueVideoActors.ToListAsync(Token));
        Assert.False(await context.CatalogueImages.AnyAsync(Token));
        Assert.Empty(api.AskedFor("/videos/batch"));
    }

    [Fact]
    public async Task Unrelated_history_is_discarded_but_its_cursor_advances_and_known_tombstones_keep_metadata()
    {
        var unrelated = Guid.NewGuid();
        var api = new FakePrdbApi().Answers("/videos/changes", Empty(Stamp))
            .Answers("/videos/changes", Change(unrelated, Stamp.AddMinutes(1), false))
            .Answers("/videos/changes", Change(Video, Stamp.AddMinutes(2), true));
        await using var db = await CreateAsync(api);
        await RunAsync<VideoFeedRoutine>(db);
        await RunAsync<VideoFeedRoutine>(db);
        await using (var scope = db.Scope())
        {
            var cursors = scope.ServiceProvider.GetRequiredService<FeedCursors>();
            Assert.Equal(unrelated, (await cursors.PositionAsync(Feed.VideoChanges, Token))!.SinceId);
        }
        await RunAsync<VideoFeedRoutine>(db);
        await using var after = db.Scope();
        var video = await after.ServiceProvider.GetRequiredService<FabDbContext>().CatalogueVideos.SingleAsync(Token);
        Assert.Equal("Original", video.Title);
        Assert.False(video.Available);
        Assert.Equal(Guid.Empty, video.MergedIntoId);
    }

    [Fact]
    public async Task Governor_deferral_preserves_the_lower_bound_and_incomplete_baseline()
    {
        var api = new FakePrdbApi { Hourly = (100, 12, 60) }.Answers("/videos/changes", Empty(Stamp));
        await using var db = await CreateAsync(api);
        await RunAsync<VideoFeedRoutine>(db);
        await Assert.ThrowsAsync<Prdb.Fab.Infrastructure.Connections.PrdbDeferredException>(() => RunAsync<VideoFeedRoutine>(db));
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Null((await context.VideoChangeState.SingleAsync(Token)).BaselineCompletedAt);
        Assert.Equal(Stamp, (await scope.ServiceProvider.GetRequiredService<FeedCursors>().PositionAsync(Feed.VideoChanges, Token))!.At);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task Lost_cursor_invalidates_freshness_and_restarts_a_local_baseline_without_walking_history()
    {
        var api = new FakePrdbApi().Answers("/videos/changes", Empty(Stamp))
            .Answers("/videos/changes", Empty(Stamp.AddHours(1)))
            .Answers("/videos/batch", $$$"""[{"id":"{{{Video}}}","title":"Current","updatedAtUtc":"{{{Stamp:O}}}"}]""");
        await using var db = await CreateAsync(api);
        await RunAsync<VideoFeedRoutine>(db);
        await RunAsync<CatalogueRepairRoutine>(db);
        await RunAsync<CatalogueRepairRoutine>(db);
        await using (var scope = db.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().FeedCursors.Where(c => c.Feed == Feed.VideoChanges).ExecuteDeleteAsync(Token);
            Assert.False(await scope.ServiceProvider.GetRequiredService<VideoChangeCoverage>().FreshAsync(Token));
        }
        await RunAsync<VideoFeedRoutine>(db);
        await using var after = db.Scope();
        var context = after.ServiceProvider.GetRequiredService<FabDbContext>();
        var state = await context.VideoChangeState.SingleAsync(Token);
        Assert.Equal(Stamp.AddHours(1), state.LowerBoundAt);
        Assert.Null(state.BaselineCompletedAt);
        Assert.Null((await context.CatalogueVideos.SingleAsync(Token)).ChangesBaselineAt);
        Assert.Contains("PageSize=1", api.AskedFor("/videos/changes")[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi api)
    {
        var db = await TestDatabase.CreateAsync(prdb: api, also: s => s.AddFabSync());
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(u => u.SetProperty(i => i.PrdbApiKey, "fixture"), Token);
        var video = new CatalogueVideoRow { PrdbId = Video, Title = "Original", CreatedAtUtc = Stamp.AddYears(-1), TitleSearchedBackwards = true };
        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(Token);
        context.WantedVideos.Add(new WantedVideoRow { VideoId = video.Id });
        context.CatalogueImages.Add(new CatalogueImageRow { PrdbId = Image, VideoId = video.Id, Url = "https://cdn.invalid/image.jpg" });
        await context.SaveChangesAsync(Token);
        return db;
    }
    private static async Task RunAsync<T>(TestDatabase db) where T : Prdb.Fab.Core.Scheduling.IRoutine
    {
        await using var scope = db.Scope();
        await scope.ServiceProvider.GetRequiredService<T>().RunAsync(null, Token);
    }
    private static string Empty(DateTimeOffset at) => $$"""{"items":[],"hasMore":false,"serverTimeUtc":"{{at:O}}","nextCursor":null}""";
    private static string Change(Guid id, DateTimeOffset at, bool deleted) => $$$"""
        {"items":[{"eventType":"{{{(deleted ? "deleted" : "updated")}}}","video":{"id":"{{{id}}}","title":"Unrelated","createdAtUtc":"{{{Stamp.AddYears(-1):O}}}","updatedAtUtc":"{{{at:O}}}","isDeleted":{{{(deleted ? "true" : "false")}}},"mergedIntoId":"{{{(id == Video ? Guid.Empty : id)}}}"}}],"hasMore":false,"serverTimeUtc":"{{{at:O}}}","nextCursor":{"id":"{{{id}}}","updatedAtUtc":"{{{at:O}}}"}}
        """;
}
