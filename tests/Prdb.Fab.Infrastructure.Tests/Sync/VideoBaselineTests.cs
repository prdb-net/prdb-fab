using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class VideoBaselineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Bound = Now.AddMinutes(-2);
    private static readonly Guid First = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_full_document_page_replaces_thirty_discovery_and_hydration_calls_and_hands_off_inclusively()
    {
        var items = Enumerable.Range(1, 1000).Select(i => Video(Guid.Parse($"bbbbbbbb-0000-4000-8000-{i:D12}")));
        var api = new FakePrdbApi().Answers("/videos/baseline", Page(items, false))
            .Answers("/videos/changes", Empty(Now));
        await using var db = await CreateAsync(api);
        await RunAsync<RecentCatalogueRoutine>(db);
        Assert.Single(api.AskedFor("/videos/baseline"));
        Assert.Empty(api.AskedFor("/videos"));
        Assert.Empty(api.AskedFor("/videos/batch"));
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(1000, await context.CatalogueVideos.CountAsync(Token));
            Assert.Equal("catch-up", (await context.RecentWindowState.SingleAsync(Token)).BaselinePhase);
            Assert.NotNull((await context.VideoChangeState.SingleAsync(Token)).BaselineCompletedAt);
            Assert.False(await scope.ServiceProvider.GetRequiredService<VideoChangeCoverage>().FreshAsync(Token));
        }
        await RunAsync<VideoFeedRoutine>(db);
        await RunAsync<CatalogueRepairRoutine>(db);
        await RunAsync<RecentCatalogueRoutine>(db);
        Assert.Empty(api.AskedFor("/videos/batch"));
        Assert.Single(api.AskedFor("/videos/baseline"));
        var query = HttpUtility.ParseQueryString(Assert.Single(api.AskedFor("/videos/changes")).Query);
        Assert.Equal(Bound, DateTimeOffset.Parse(query["Since"]!));
        Assert.Null(query["SinceId"]);
        await using var after = db.Scope();
        Assert.True(await after.ServiceProvider.GetRequiredService<VideoChangeCoverage>().FreshAsync(Token));
        Assert.True((await after.ServiceProvider.GetRequiredService<RecentWindowCoverage>().ReadAsync(Token)).Catalogue.Complete);
    }

    [Fact]
    public async Task Committed_scope_and_opaque_cursor_survive_lost_responses_and_resume_with_the_same_range()
    {
        var api = new FakePrdbApi().Answers("/videos/baseline", Page([Video(First)], true, "opaque +/&="))
            .IsUnreachable("/videos/baseline")
            .Answers("/videos/baseline", Page([Video(Second)], false));
        await using var db = await CreateAsync(api);
        await RunAsync<RecentCatalogueRoutine>(db);
        db.Time.Advance(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync<RecentCatalogueRoutine>(db));
        await RunAsync<RecentCatalogueRoutine>(db);
        var requests = api.AskedFor("/videos/baseline").Select(u => HttpUtility.ParseQueryString(u.Query)).ToArray();
        Assert.Null(requests[0]["Cursor"]);
        Assert.All(requests.Skip(1), q => Assert.Equal("opaque +/&=", q["Cursor"]));
        Assert.All(requests, q =>
        {
            Assert.Equal(Now.AddDays(-90), DateTimeOffset.Parse(q["CreatedFrom"]!));
            Assert.Equal(Now, DateTimeOffset.Parse(q["CreatedBefore"]!));
        });
        await using var scope = db.Scope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<FabDbContext>().CatalogueVideos.CountAsync(Token));
    }

    [Fact]
    public async Task Catch_up_applies_edits_deletions_new_rows_and_merge_survivors_before_claiming_live_coverage()
    {
        var third = Guid.NewGuid();
        var survivor = Guid.NewGuid();
        var api = new FakePrdbApi().Answers("/videos/baseline", Page([Video(First), Video(Second)], false))
            .Answers("/videos/changes", JsonSerializer.Serialize(new
            {
                items = new object[]
                {
                    new { eventType = "deleted", video = new { id = First, updatedAtUtc = Now, isDeleted = true, mergedIntoId = survivor } },
                    new { eventType = "updated", video = Video(Second, "Corrected", Now) },
                    new { eventType = "updated", video = Video(third, "Inserted", Now) },
                },
                hasMore = false, serverTimeUtc = Now, nextCursor = new { id = third, updatedAtUtc = Now },
            }))
            .Answers("/videos/batch", JsonSerializer.Serialize(new[] { Video(survivor, "Survivor", Now) }));
        await using var db = await CreateAsync(api);
        await RunAsync<RecentCatalogueRoutine>(db);
        await RunAsync<VideoFeedRoutine>(db);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var rows = await context.CatalogueVideos.ToDictionaryAsync(v => v.PrdbId, Token);
        Assert.False(rows[First].Available);
        Assert.Equal(survivor, rows[First].MergedIntoId);
        Assert.Equal("Corrected", rows[Second].Title);
        Assert.Equal("Inserted", rows[third].Title);
        Assert.Equal("Survivor", rows[survivor].Title);
        // A replayed older full document cannot resurrect the tombstone.
        await scope.ServiceProvider.GetRequiredService<VideoDetails>().WriteAsync(new()
        { Id = First, Title = "Late", CreatedAtUtc = Now.AddDays(-1), UpdatedAtUtc = Bound }, Token);
        Assert.False((await context.CatalogueVideos.SingleAsync(v => v.PrdbId == First, Token)).Available);
        Assert.Equal("live", (await context.RecentWindowState.SingleAsync(Token)).BaselinePhase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typed_oversize_escape_is_durable_across_deferral_and_a_missing_document_is_reconciled_through_changes(bool missing)
    {
        var api = new FakePrdbApi().RefusesWithBody("/videos/baseline", HttpStatusCode.RequestEntityTooLarge,
                JsonSerializer.Serialize(new { videoId = First, nextCursor = "after-large", changesSince = Bound }))
            .Answers("/videos/baseline", Page([], false))
            .Answers("/videos/changes", Empty(Now));
        if (missing) api.Refuses($"/videos/{First}", HttpStatusCode.NotFound, "Gone");
        else api.Answers($"/videos/{First}", JsonSerializer.Serialize(Video(First)));
        await using var db = await CreateAsync(api);
        await RunAsync<RecentCatalogueRoutine>(db);
        var governor = db.Services.GetRequiredService<PrdbGovernor>();
        Observe(governor, 0);
        await Assert.ThrowsAsync<PrdbDeferredException>(() => RunAsync<RecentCatalogueRoutine>(db));
        Observe(governor, 1000);
        await RunAsync<RecentCatalogueRoutine>(db);
        await RunAsync<RecentCatalogueRoutine>(db);
        await RunAsync<VideoFeedRoutine>(db);
        Assert.Equal("after-large", HttpUtility.ParseQueryString(api.AskedFor("/videos/baseline")[1].Query)["Cursor"]);
        Assert.Single(api.AskedFor($"/videos/{First}"));
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(!missing, (await context.CatalogueVideos.SingleAsync(Token)).Available);
        Assert.Equal("live", (await context.RecentWindowState.SingleAsync(Token)).BaselinePhase);
    }

    [Fact]
    public async Task Invalid_or_backwards_feed_pages_do_not_move_the_baseline_handoff()
    {
        var api = new FakePrdbApi().Answers("/videos/baseline", Page([Video(First)], false))
            .Answers("/videos/changes", Empty(Bound.AddMinutes(-1)));
        await using var db = await CreateAsync(api);
        await RunAsync<RecentCatalogueRoutine>(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync<VideoFeedRoutine>(db));
        await using var scope = db.Scope();
        Assert.Equal(Bound, (await scope.ServiceProvider.GetRequiredService<FeedCursors>().PositionAsync(Feed.VideoChanges, Token))!.At);
        Assert.Equal("catch-up", (await scope.ServiceProvider.GetRequiredService<FabDbContext>().RecentWindowState.SingleAsync(Token)).BaselinePhase);
    }

    [Fact]
    public async Task A_failed_local_page_rolls_back_documents_and_cursor_and_retries_from_the_committed_position()
    {
        var api = new FakePrdbApi().Answers("/videos/baseline", Page([Video(First), Video(Second)], false));
        await using var db = await CreateAsync(api);
        await using (var scope = db.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_second BEFORE INSERT ON catalogue_video WHEN NEW.Title = 'Complete' "
                + "AND (SELECT COUNT(*) FROM catalogue_video) = 1 BEGIN SELECT RAISE(ABORT, 'fixture interruption'); END;", Token);
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync<RecentCatalogueRoutine>(db));
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Empty(await context.CatalogueVideos.ToListAsync(Token));
            var state = await context.RecentWindowState.SingleAsync(Token);
            Assert.Null(state.BaselineCursor);
            Assert.Null(state.BaselineChangesSince);
            Assert.Equal("scan", state.BaselinePhase);
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_second;", Token);
        }
        await RunAsync<RecentCatalogueRoutine>(db);
        await using var after = db.Scope();
        Assert.Equal(2, await after.ServiceProvider.GetRequiredService<FabDbContext>().CatalogueVideos.CountAsync(Token));
    }

    [Fact]
    public async Task Losing_the_live_cursor_invalidates_coverage_and_requests_a_new_inventory()
    {
        var api = new FakePrdbApi().Answers("/videos/baseline", Page([Video(First)], false))
            .Answers("/videos/changes", Empty(Now));
        await using var db = await CreateAsync(api);
        await RunAsync<RecentCatalogueRoutine>(db);
        await RunAsync<VideoFeedRoutine>(db);
        await using (var scope = db.Scope())
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().FeedCursors
                .Where(c => c.Feed == Feed.VideoChanges).ExecuteDeleteAsync(Token);
        await RunAsync<VideoFeedRoutine>(db);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Null((await context.RecentWindowState.SingleAsync(Token)).BaselinePhase);
            Assert.Null((await context.VideoChangeState.SingleAsync(Token)).BaselineCompletedAt);
            Assert.False(await scope.ServiceProvider.GetRequiredService<VideoChangeCoverage>().FreshAsync(Token));
        }
        await RunAsync<RecentCatalogueRoutine>(db);
        Assert.Equal(2, api.AskedFor("/videos/baseline").Count);
    }

    private static void Observe(PrdbGovernor governor, int remaining)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-RateLimit-Limit-Hour", "1000");
        response.Headers.Add("X-RateLimit-Remaining-Hour", remaining.ToString());
        response.Headers.Add("X-RateLimit-Reset-Hour", "60");
        governor.Observe(response);
    }

    private static object Video(Guid id, string title = "Complete", DateTimeOffset? updated = null) => new
    { id, title, createdAtUtc = Now.AddDays(-1), updatedAtUtc = updated ?? Bound, images = new object[0], actors = new object[0], preNames = new object[0] };
    private static string Page(IEnumerable<object> items, bool more, string? cursor = null) => JsonSerializer.Serialize(new
    { items, hasMore = more, nextCursor = cursor, changesSince = Bound });
    private static string Empty(DateTimeOffset at) => JsonSerializer.Serialize(new { items = new object[0], hasMore = false, serverTimeUtc = at });
    private static async Task<TestDatabase> CreateAsync(FakePrdbApi api)
    {
        var db = await TestDatabase.CreateAsync(prdb: api, also: s => s.AddFabSync());
        await using var scope = db.Scope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.PrdbApiKey, "fixture"), Token);
        return db;
    }
    private static async Task<RunResult> RunAsync<T>(TestDatabase db) where T : IRoutine
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<T>().RunAsync(null, Token);
    }
}
