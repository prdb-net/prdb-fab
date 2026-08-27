using System.Globalization;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0013's five change feeds: what a page does to the database, and what the
/// request after it asks for.
/// </summary>
/// <remarks>
/// Everything above the socket is the real thing (ADR 0042) — the SDK builds
/// the request, the governor's handler sees it, the routine applies the page and
/// writes the cursor. The query strings these assertions read are the ones that
/// would have gone to prdb.
/// </remarks>
public sealed class ChangeFeedTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Actors = "/actors/changes";
    private const string Images = "/videos/images/changes";
    private const string Wanted = "/wanted-videos/changes";

    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid JaneDoe = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid AVideo = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid AnImage = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>
    /// ADR 0013's overlap, through the whole stack: a feed sitting at the last
    /// value it saw asks from before it, and the row sharing that timestamp is
    /// still there afterwards. Without it a bulk import loses every row that
    /// landed in the same second as the boundary, permanently and silently.
    /// </summary>
    [Fact]
    public async Task A_feed_is_replayed_from_before_its_own_cursor()
    {
        var prdb = new FakePrdbApi().Answers(Actors, ActorPage(JaneDoe, "Jane Doe", hasMore: false, at: Noon));

        await using var database = await CreateAsync(prdb);

        await RunAsync<ActorFeedRoutine>(database);
        await RunAsync<ActorFeedRoutine>(database);

        var asked = prdb.AskedFor(Actors);

        Assert.Equal(2, asked.Count);

        // The first run had nothing to resume from, so it started at the
        // beginning of the feed — which is the documented bootstrap.
        Assert.Null(Query(asked[0], "Since"));

        // The second asked from a minute before where the first left off, and
        // without the tie-breaker, which only means anything at the exact
        // timestamp it came from.
        Assert.Equal(Noon - FeedPosition.Overlap, Time(Query(asked[1], "Since")));
        Assert.Null(Query(asked[1], "SinceId"));

        // And the row that was there at the boundary is still there.
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(1, await context.CatalogueActors.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The other half of ADR 0013's rule, and what makes the overlap safe:
    /// every result is an idempotent upsert, so re-delivery is not a bug and
    /// applying the same page twice is applying it once.
    /// </summary>
    [Fact]
    public async Task Running_a_feed_twice_over_one_page_changes_nothing()
    {
        var prdb = new FakePrdbApi().Answers(Actors, ActorPage(JaneDoe, "Jane Doe", hasMore: false, at: Noon));

        await using var database = await CreateAsync(prdb);

        await RunAsync<ActorFeedRoutine>(database);
        await RunAsync<ActorFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var actor = await context.CatalogueActors.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(JaneDoe, actor.PrdbId);
        Assert.Equal("Jane Doe", actor.Name);
    }

    /// <summary>
    /// The images feed is global and the catalogue is a fraction of it. Keeping
    /// a row for a video nobody here holds would make the image table a
    /// multiple of the table it describes, and it costs nothing to drop:
    /// ADR 0013 has no catalogue row arriving without a detail read, and a
    /// detail read brings <c>images[]</c> with it.
    /// </summary>
    [Fact]
    public async Task An_image_for_a_video_the_catalogue_does_not_hold_leaves_no_row()
    {
        var prdb = new FakePrdbApi()
            // The first run is the one that takes prdb's clock; the second is
            // the one carrying the image.
            .Answers(Images, ImagePage(AnImage, AVideo, "https://example.invalid/a.jpg", at: Noon))
            .Answers(Images, ImagePage(AnImage, AVideo, "https://example.invalid/a.jpg", at: Noon));

        await using var database = await CreateAsync(prdb);

        await RunAsync<VideoImageFeedRoutine>(database);
        await RunAsync<VideoImageFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.CatalogueImages.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// And the images feed does not read history at all. Draining prdb's global
    /// image corpus would be the most expensive thing this tool ever did and
    /// would discard almost all of it, so the first run spends one request on
    /// the server's clock — the only lower bound prdb will read back as its own
    /// — and starts there.
    /// </summary>
    [Fact]
    public async Task The_images_feed_starts_at_what_prdb_has_now()
    {
        var prdb = new FakePrdbApi()
            .Answers(Images, ImagePage(AnImage, AVideo, "https://example.invalid/a.jpg", at: Noon));

        await using var database = await CreateAsync(prdb);

        await RunAsync<VideoImageFeedRoutine>(database);

        var first = prdb.AskedFor(Images).Single();

        Assert.Null(Query(first, "Since"));
        Assert.Equal("1", Query(first, "PageSize"));

        await using var scope = database.Scope();
        var cursors = scope.ServiceProvider.GetRequiredService<FeedCursors>();

        var position = await cursors.PositionAsync(Feed.VideoImages, TestContext.Current.CancellationToken);

        // The page's server time, not the row's — the rows in it are history
        // this installation has no use for.
        Assert.Equal(Noon, position?.At);
    }

    /// <summary>
    /// ADR 0033 keys the wanted list by the catalogue row, so the row has to
    /// exist before the list can point at it, and the feed's own payload is
    /// what fills it. Nothing is lost by that: the row says it has never been
    /// read in detail, and a wanted video is pinned, so ADR 0013's repair pass —
    /// which takes pinned rows oldest-checked first — takes it before anything
    /// else.
    /// </summary>
    [Fact]
    public async Task A_wanted_video_brings_the_catalogue_row_it_points_at()
    {
        var prdb = new FakePrdbApi().Answers(Wanted, WantedPage(AVideo, "A Video", deleted: false, at: Noon));

        await using var database = await CreateAsync(prdb);

        await RunAsync<WantedVideoFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = await context.CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AVideo, video.PrdbId);
        Assert.Equal("A Video", video.Title);
        Assert.Equal("a video", video.NormalisedTitle);
        Assert.Equal(default, video.LastReadAt);
        Assert.Null(video.SiteId);

        var wanted = await context.WantedVideos.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(video.Id, wanted.VideoId);
    }

    /// <summary>
    /// Struck off the list in prdb, and the catalogue row stays. It belongs to
    /// no account, something else may still point at it, and losing a pin is
    /// what eviction is for rather than what a delete is.
    /// </summary>
    [Fact]
    public async Task A_video_struck_off_the_wanted_list_leaves_the_catalogue_alone()
    {
        var prdb = new FakePrdbApi()
            .Answers(Wanted, WantedPage(AVideo, "A Video", deleted: false, at: Noon))
            .Answers(Wanted, WantedPage(AVideo, "A Video", deleted: true, at: Noon.AddMinutes(5)));

        await using var database = await CreateAsync(prdb);

        await RunAsync<WantedVideoFeedRoutine>(database);
        await RunAsync<WantedVideoFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.WantedVideos.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CatalogueVideos.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0014: bootstrap is not a state of the application. The drain runs
    /// beside the recurring feeds, resumes where a restart interrupted it, and
    /// when it is done it stops existing — including across the restart that
    /// would otherwise create its row again and read prdb's whole actor corpus
    /// a second time.
    /// </summary>
    [Fact]
    public async Task The_actors_drain_resumes_across_a_restart_and_retires_exactly_once()
    {
        var walking = ActorPage(JaneDoe, "Jane Doe", hasMore: true, at: Noon);
        var caughtUp = ActorPage(JaneDoe, "Jane Doe", hasMore: false, at: Noon.AddMinutes(1));

        var prdb = new FakePrdbApi().Answers(Actors, walking).Answers(Actors, caughtUp);

        await using var database = await CreateAsync(prdb);

        await StartAsync(database);
        Assert.True(await HasRowAsync(database, ActorDrainRoutine.RoutineName));

        // One page, and there is more behind it. The position keeps the row it
        // stopped at, so the next request resumes from exactly there.
        await RunAsync<ActorDrainRoutine>(database);

        await StartAsync(database);
        Assert.True(await HasRowAsync(database, ActorDrainRoutine.RoutineName));

        await using (var scope = database.Scope())
        {
            var position = await scope.ServiceProvider
                .GetRequiredService<FeedCursors>()
                .PositionAsync(Feed.Actors, TestContext.Current.CancellationToken);

            Assert.NotNull(position?.Unfinished);
        }

        // The second page is the end of the feed, so the drain retires.
        await RunAsync<ActorDrainRoutine>(database);

        Assert.False(await HasRowAsync(database, ActorDrainRoutine.RoutineName));

        // And a restart does not bring it back. The recurring routine reads on
        // from the position it left.
        await StartAsync(database);

        Assert.False(await HasRowAsync(database, ActorDrainRoutine.RoutineName));
        Assert.True(await HasRowAsync(database, ActorFeedRoutine.RoutineName));

        // The second request resumed from exactly where the first stopped:
        // mid-walk, no overlap, because a walk that is still running would
        // otherwise be replayed from the same place for ever.
        var asked = prdb.AskedFor(Actors);

        Assert.Equal(Noon, Time(Query(asked[1], "Since")));
        Assert.Equal(JaneDoe.ToString(), Query(asked[1], "SinceId"));
    }

    /// <summary>
    /// Before onboarding has reached ADR 0010's prdb step there is no key, and
    /// a feed with no key has no work rather than a failure — ADR 0032's empty
    /// tick, which is not a run.
    /// </summary>
    [Fact]
    public async Task A_feed_with_no_prdb_key_is_not_a_run()
    {
        var prdb = new FakePrdbApi().Answers(Actors, ActorPage(JaneDoe, "Jane Doe", hasMore: false, at: Noon));

        await using var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddFabSync());

        await using var scope = database.Scope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ActorFeedRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        Assert.False(result.IsRecorded);
        Assert.Empty(prdb.Asked);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddFabSync());

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        await context.Installation.ExecuteUpdateAsync(
            row => row.SetProperty(installation => installation.PrdbApiKey, ApiKey),
            TestContext.Current.CancellationToken);

        return database;
    }

    /// <summary>
    /// What a container does on the way up: the rows for the routines this build
    /// knows about, and then ADR 0014's spread over the overdue ones.
    /// </summary>
    private static Task StartAsync(TestDatabase database) =>
        database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);

    private static async Task RunAsync<TRoutine>(TestDatabase database)
        where TRoutine : class, IRoutine
    {
        await using var scope = database.Scope();

        var result = await scope.ServiceProvider
            .GetRequiredService<TRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Succeeded, result.Outcome);
    }

    private static async Task<bool> HasRowAsync(TestDatabase database, string name)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .Routines
            .AnyAsync(row => row.Name == name, TestContext.Current.CancellationToken);
    }

    private static string? Query(Uri uri, string name) =>
        HttpUtility.ParseQueryString(uri.Query)[name];

    private static DateTimeOffset? Time(string? text) =>
        text is null
            ? null
            : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string ActorPage(Guid id, string name, bool hasMore, DateTimeOffset at) =>
        $$"""
        {
          "items": [
            {
              "eventType": "updated",
              "actor": { "id": "{{id}}", "name": "{{name}}", "isDeleted": false }
            }
          ],
          "pageSize": 1000,
          "hasMore": {{(hasMore ? "true" : "false")}},
          "serverTimeUtc": "{{Stamp(at)}}",
          "nextCursor": { "updatedAtUtc": "{{Stamp(at)}}", "id": "{{id}}" }
        }
        """;

    private static string ImagePage(Guid id, Guid videoId, string url, DateTimeOffset at) =>
        $$"""
        {
          "items": [
            {
              "eventType": "created",
              "image": {
                "id": "{{id}}",
                "videoId": "{{videoId}}",
                "url": "{{url}}",
                "createdAtUtc": "{{Stamp(at)}}",
                "updatedAtUtc": "{{Stamp(at)}}"
              }
            }
          ],
          "pageSize": 1,
          "hasMore": false,
          "serverTimeUtc": "{{Stamp(at)}}",
          "nextCursor": { "updatedAtUtc": "{{Stamp(at)}}", "id": "{{id}}" }
        }
        """;

    private static string WantedPage(Guid videoId, string title, bool deleted, DateTimeOffset at) =>
        $$"""
        {
          "items": [
            {
              "eventType": "{{(deleted ? "deleted" : "created")}}",
              "wantedVideo": {
                "videoId": "{{videoId}}",
                "videoTitle": "{{title}}",
                "siteTitle": "A Site",
                "isDeleted": {{(deleted ? "true" : "false")}},
                "isFulfilled": false,
                "createdAtUtc": "{{Stamp(at)}}",
                "updatedAtUtc": "{{Stamp(at)}}"
              }
            }
          ],
          "pageSize": 1000,
          "hasMore": false,
          "serverTimeUtc": "{{Stamp(at)}}",
          "nextCursor": { "updatedAtUtc": "{{Stamp(at)}}", "id": "{{videoId}}" }
        }
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
