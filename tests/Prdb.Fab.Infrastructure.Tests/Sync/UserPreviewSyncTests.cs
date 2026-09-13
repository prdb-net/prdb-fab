using System.Globalization;
using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0061's synchronisation: who asks, how often, and what a withdrawal does.
/// </summary>
/// <remarks>
/// Everything above the socket is real (ADR 0042) — the SDK builds the request,
/// the governor's handler sees it, the routine applies the answer and writes the
/// rows. What these are about is the two writers meeting: a snapshot of one
/// Video and a global feed, at different cadences in different lanes.
/// </remarks>
public sealed class UserPreviewSyncTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Changes = "/video-user-images/changes";
    private const string Hash = "A1B2C3D4E5F60718";

    private static readonly Guid Video = Guid.Parse("aaaa0000-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("aaaa0000-0000-4000-8000-000000000002");
    private static readonly Guid Preview = Guid.Parse("bbbb0000-0000-4000-8000-000000000001");
    private static readonly Guid User = Guid.Parse("cccc0000-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static string ByVideo(Guid videoId) => $"/videos/{videoId:D}/user-images";

    /// <summary>
    /// The whole interactive path: an ask that spends nothing, a routine row
    /// that is the record of it, one request, and rows where there were none.
    /// </summary>
    [Fact]
    public async Task Opening_a_preview_asks_once_and_the_read_fills_the_gallery()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), $"[{Sprite()}]");

        await using var database = await CreateAsync(prdb);

        Assert.Equal(UserPreviewOutcome.Asked, await AskAsync(database, UserPreviewDemand.Preview));

        // The sheet never waits on prdb: the ask writes a row and returns.
        Assert.Empty(prdb.AskedFor(ByVideo(Video)));

        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        Assert.Single(prdb.AskedFor(ByVideo(Video)));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var row = await context.UserPreviews.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Preview, row.PrdbId);
        Assert.Equal(Video, row.VideoPrdbId);
        Assert.Equal(Hash, row.OsHash);
        Assert.True(row.Shown);

        // The row retires, so the next ask is a fresh decision rather than a
        // second request.
        Assert.False(await context.Routines.AnyAsync(
            row => UserPreviewReadRoutine.Names.Contains(row.Name),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The handoff ADR 0061 fixes the order of: the feed's position is taken
    /// from prdb's own clock <em>before</em> the first snapshot, so that a
    /// withdrawal landing between the two is replayed rather than lost.
    /// </summary>
    [Fact]
    public async Task The_feeds_clock_is_taken_before_the_first_snapshot()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), "[]");

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        var asked = prdb.Asked;

        Assert.Equal(Changes, asked[0].AbsolutePath);
        Assert.Equal(ByVideo(Video), asked[1].AbsolutePath);

        await using var scope = database.Scope();
        var cursors = scope.ServiceProvider.GetRequiredService<FeedCursors>();

        var position = await cursors.PositionAsync(
            Feed.VideoUserImages,
            TestContext.Current.CancellationToken);

        Assert.Equal(Noon, position!.At);

        // And it is taken once. A second Video's read finds a position and
        // spends nothing on the handoff.
        await AskAsync(database, UserPreviewDemand.Preview, Other);

        Assert.Single(prdb.AskedFor(Changes));
    }

    /// <summary>
    /// The ordinary answer is <em>none</em>, and it is worth remembering: a
    /// Video asked about inside the freshness window is not asked about again,
    /// however often its sheet is opened.
    /// </summary>
    [Fact]
    public async Task An_empty_answer_is_remembered_for_a_week_and_then_asked_again()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), "[]");

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        Assert.Equal(UserPreviewOutcome.Fresh, await AskAsync(database, UserPreviewDemand.Preview));
        Assert.Equal(UserPreviewOutcome.Fresh, await AskAsync(database, UserPreviewDemand.Library));

        database.Time.Advance(UserPreviewContract.Freshness + TimeSpan.FromMinutes(1));

        Assert.Equal(UserPreviewOutcome.Asked, await AskAsync(database, UserPreviewDemand.Preview));
        Assert.Single(prdb.AskedFor(ByVideo(Video)));
    }

    /// <summary>
    /// Two demands over one Video are one read. A Preview opened over a Video
    /// the Library is already enriching moves the row into the Sync lane rather
    /// than adding a second one — deduplicated, and still the thing somebody is
    /// waiting on.
    /// </summary>
    [Fact]
    public async Task Filing_and_a_preview_of_one_video_schedule_a_single_read()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), "[]");

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Library);
        await AskAsync(database, UserPreviewDemand.Preview);
        await AskAsync(database, UserPreviewDemand.Preview);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            var rows = await context.Routines
                .Where(row => UserPreviewReadRoutine.Names.Contains(row.Name))
                .ToListAsync(TestContext.Current.CancellationToken);

            var row = Assert.Single(rows);

            Assert.Equal(PreviewUserPreviewRoutine.RoutineName, row.Name);
            Assert.Equal(Lane.Sync, row.Lane);

            // And the Library's reason is still recorded, which is what keeps
            // the interest from expiring with a browse.
            var interest = await context.UserPreviewInterests.SingleAsync(
                TestContext.Current.CancellationToken);

            Assert.True(interest.ForTheLibrary);
        }

        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        Assert.Single(prdb.AskedFor(ByVideo(Video)));
    }

    /// <summary>
    /// A spent budget defers the read rather than failing it, and leaves every
    /// durable row as it was — so the routine row is still there after a
    /// restart and the interest still says the read is due.
    /// </summary>
    [Fact]
    public async Task A_deferred_read_leaves_the_work_where_a_restart_finds_it()
    {
        var prdb = new FakePrdbApi { Hourly = (Limit: 100, Remaining: 1, ResetInSeconds: 30) }
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), "[]");

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Library);

        // One answered request, so the governor has a reading to refuse from.
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<Prdb.Fab.Infrastructure.Connections.PrdbGateway>()
                .CheckAsync(ApiKey, TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            await Assert.ThrowsAsync<Prdb.Fab.Infrastructure.Connections.PrdbDeferredException>(() =>
                scope.ServiceProvider.GetRequiredService<LibraryUserPreviewRoutine>()
                    .RunAsync(Video.ToString("D"), TestContext.Current.CancellationToken));
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            Assert.True(await context.Routines.AnyAsync(
                row => row.Name == LibraryUserPreviewRoutine.RoutineName,
                TestContext.Current.CancellationToken));

            var interest = await context.UserPreviewInterests.SingleAsync(
                TestContext.Current.CancellationToken);

            Assert.Null(interest.LastReadAt);
        }
    }

    /// <summary>
    /// A withdrawal on the feed stops a preview being served, and the same id
    /// coming back under the signature it was shown under restores it. This is
    /// the lifecycle ADR 0061 refuses to express with <c>FoundDead</c>.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_stops_a_preview_and_a_restoration_brings_it_back()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), $"[{Sprite()}]")
            .Answers(Changes, ChangePage(Sprite(status: "Denied", visibility: "Private", at: Noon.AddHours(1)), Noon.AddHours(1)))
            .Answers(Changes, ChangePage(Sprite(at: Noon.AddHours(2)), Noon.AddHours(2)));

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        Assert.True(await ShownAsync(database));

        await RunAsync<UserPreviewFeedRoutine>(database);

        Assert.False(await ShownAsync(database));

        await RunAsync<UserPreviewFeedRoutine>(database);

        Assert.True(await ShownAsync(database));
    }

    /// <summary>
    /// The rule that makes the two writers commutative: a snapshot whose
    /// payload is older than what is held does not undo a withdrawal that
    /// arrived first. Without it the two lanes racing would restore a picture a
    /// moderator removed, intermittently.
    /// </summary>
    [Fact]
    public async Task A_snapshot_older_than_a_withdrawal_does_not_restore_it()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), $"[{Sprite()}]")
            .Answers(Changes, ChangePage(Sprite(status: "Denied", visibility: "Private", at: Noon.AddHours(1)), Noon.AddHours(1)))
            // The answer to a request that went out before the withdrawal and
            // came back after it.
            .Answers(ByVideo(Video), $"[{Sprite()}]");

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);
        await RunAsync<UserPreviewFeedRoutine>(database);

        Assert.False(await ShownAsync(database));

        database.Time.Advance(UserPreviewContract.Freshness + TimeSpan.FromMinutes(1));

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        Assert.False(await ShownAsync(database));
    }

    /// <summary>
    /// A page replayed inside ADR 0013's overlap — the same rows at the same
    /// timestamp — writes the same values and creates nothing.
    /// </summary>
    [Fact]
    public async Task A_page_replayed_at_the_same_timestamp_leaves_one_row()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), $"[{Sprite()}]")
            .Answers(Changes, ChangePage(Sprite(), Noon));

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        await RunAsync<UserPreviewFeedRoutine>(database);
        await RunAsync<UserPreviewFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(1, await context.UserPreviews.CountAsync(TestContext.Current.CancellationToken));
        Assert.True(await ShownAsync(database));
    }

    /// <summary>
    /// The feed is global and this installation holds a fraction of it. A row
    /// for a Video nothing is interested in is dropped without a row and
    /// without a byte.
    /// </summary>
    [Fact]
    public async Task A_change_about_an_uninteresting_video_writes_nothing()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), "[]")
            .Answers(Changes, ChangePage(Sprite(videoId: Other), Noon.AddHours(1)));

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);
        await RunAsync<UserPreviewFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.UserPreviews.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0032, and the reason <see cref="IdleProfile"/> does not move: an
    /// installation nobody has browsed makes no request for this at all.
    /// </summary>
    [Fact]
    public async Task The_feed_asks_for_nothing_while_nothing_is_interested()
    {
        var prdb = new FakePrdbApi().Answers(Changes, EmptyPage(Noon));

        await using var database = await CreateAsync(prdb);

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<UserPreviewFeedRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Equal(RunResult.NothingToDo, result);
        }

        Assert.Empty(prdb.Asked);
    }

    /// <summary>
    /// An interest nothing has touched for ninety days goes, and takes its rows
    /// with it — unless a Video File on disk is one of the reasons, which
    /// outlives any browse.
    /// </summary>
    [Fact]
    public async Task A_browse_expires_and_a_filed_video_does_not()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), $"[{Sprite()}]")
            .Answers(ByVideo(Other), $"[{Sprite(id: Guid.Parse("bbbb0000-0000-4000-8000-000000000002"), videoId: Other)}]");

        await using var database = await CreateAsync(prdb);

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        await AskAsync(database, UserPreviewDemand.Library, Other);
        await RunAsync<LibraryUserPreviewRoutine>(database, Other);

        database.Time.Advance(UserPreviewContract.InterestExpiry + TimeSpan.FromDays(1));

        await RunAsync<UserPreviewFeedRoutine>(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var interest = await context.UserPreviewInterests.SingleAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(Other, interest.VideoPrdbId);

        var row = await context.UserPreviews.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Other, row.VideoPrdbId);
    }

    /// <summary>
    /// Nothing in this slice touches Identification. Reading previews for a
    /// Video an Arriving File names leaves that file's Video, Confidence and
    /// state exactly where they were — deciding anything from a hash binding is
    /// ADR 0062's, and it has not been written yet.
    /// </summary>
    [Fact]
    public async Task Reading_previews_changes_no_identification()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, EmptyPage(Noon))
            .Answers(ByVideo(Video), $"[{Sprite()}]");

        await using var database = await CreateAsync(prdb);

        var arrival = Guid.Parse("dddd0000-0000-4000-8000-000000000001");

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.ArrivingFiles.Add(new ArrivingFileRow
            {
                Id = arrival,
                DownloadId = Guid.Parse("eeee0000-0000-4000-8000-000000000001"),
                IndexerId = Guid.Parse("ffff0000-0000-4000-8000-000000000001"),
                DerivedReleaseId = "a-release",
                SourcePath = "/downloads/a.mkv",
                ArrivedName = "a.mkv",
                State = Core.Filing.ArrivingFileState.AwaitingIdentification,
                Reason = Core.Filing.ArrivingFileReason.Unidentified,
                SizeBytes = 1,
                OsHash = Hash,
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await AskAsync(database, UserPreviewDemand.Preview);
        await RunAsync<PreviewUserPreviewRoutine>(database, Video);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var row = await context.ArrivingFiles.SingleAsync(TestContext.Current.CancellationToken);

            Assert.Null(row.VideoId);
            Assert.Null(row.Confidence);
            Assert.Equal(Core.Filing.ArrivingFileState.AwaitingIdentification, row.State);
        }
    }

    private static async Task<bool> ShownAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        return await context.UserPreviews.AnyAsync(
            row => row.PrdbId == Preview && row.Shown,
            TestContext.Current.CancellationToken);
    }

    private static async Task<UserPreviewOutcome> AskAsync(
        TestDatabase database,
        UserPreviewDemand demand,
        Guid? videoPrdbId = null)
    {
        await using var scope = database.Scope();

        var ask = await scope.ServiceProvider.GetRequiredService<UserPreviews>()
            .AskAsync(videoPrdbId ?? Video, demand, TestContext.Current.CancellationToken);

        return ask.Outcome;
    }

    private static async Task RunAsync<TRoutine>(TestDatabase database, Guid? target = null)
        where TRoutine : class, IRoutine
    {
        await using var scope = database.Scope();

        var result = await scope.ServiceProvider.GetRequiredService<TRoutine>()
            .RunAsync(target?.ToString("D"), TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Succeeded, result.Outcome);
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

    private static string EmptyPage(DateTimeOffset at) =>
        $$"""
        {"items":[],"pageSize":1,"hasMore":false,"serverTimeUtc":"{{Stamp(at)}}","nextCursor":null}
        """;

    private static string ChangePage(string row, DateTimeOffset at) =>
        $$"""
        {
          "items": [{"eventType":"Updated","videoUserImage":{{row}}}],
          "pageSize": 1000,
          "hasMore": false,
          "serverTimeUtc": "{{Stamp(at)}}",
          "nextCursor": {"updatedAtUtc":"{{Stamp(at)}}","id":"{{Preview:D}}"}
        }
        """;

    private static string Sprite(
        Guid? id = null,
        Guid? videoId = null,
        string status = "Approved",
        string visibility = "Public",
        DateTimeOffset? at = null) =>
        $$"""
        {
          "id": "{{id ?? Preview:D}}",
          "userId": "{{User:D}}",
          "videoId": "{{videoId ?? Video:D}}",
          "basedOnFileWithOsHash": "{{Hash}}",
          "previewImageType": "SpriteSheet",
          "filesize": 481920,
          "width": 3200,
          "height": 1800,
          "displayOrder": 0,
          "url": "https://cdn.invalid/previews/sprite.jpg",
          "hasVtt": true,
          "vttUrl": "https://cdn.invalid/previews/sprite.vtt",
          "spriteTileCount": 100,
          "spriteTileWidth": 320,
          "spriteTileHeight": 180,
          "spriteColumns": 10,
          "spriteRows": 10,
          "moderationStatus": "{{status}}",
          "moderationVisibility": "{{visibility}}",
          "isDeleted": false,
          "createdAtUtc": "{{Stamp(Noon.AddDays(-1))}}",
          "updatedAtUtc": "{{Stamp(at ?? Noon)}}"
        }
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
