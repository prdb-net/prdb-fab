using System.Globalization;
using System.Web;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0061's contract, asserted against the SDK this repository references
/// rather than against the document it was generated from.
/// </summary>
/// <remarks>
/// <para>
/// These are fixtures rather than behaviour: no routine runs, nothing is
/// written, and what is being checked is that the four operations the contract
/// rests on exist in <c>Prdb.Sdk</c> 0.13.0, are addressed where the document
/// says, and hand back the fields the design reads. A contract nobody executes
/// is a contract that drifts, and this is the cheapest thing that notices.
/// </para>
/// <para>
/// Everything above the socket is real (ADR 0042) — Kiota builds the request,
/// the governor's handler sees it, and the deserialisation is the SDK's own. The
/// bodies below are sanitized: made-up ids, a CDN host that does not exist, and
/// no user identity beyond the opaque <c>userId</c> the payload carries.
/// </para>
/// </remarks>
public sealed class UserPreviewContractTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Changes = "/video-user-images/changes";

    private static readonly Guid AVideo = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid ASprite = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid AStill = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid AnUnlinkedPreview = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid AUser = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static string ByVideo(Guid videoId) => $"/videos/{videoId:D}/user-images";

    private static string ByHash(string hash) => $"/video-user-images/by-os-hash/{hash}";

    /// <summary>
    /// A Video nobody has submitted a preview for answers with an empty array
    /// rather than a 404 — which is what makes <em>none</em> an answer worth
    /// remembering for a week (ADR 0061's freshness) instead of a failure worth
    /// retrying.
    /// </summary>
    [Fact]
    public async Task A_video_with_no_user_previews_answers_with_an_empty_list()
    {
        var prdb = new FakePrdbApi().Answers(ByVideo(AVideo), "[]");

        await using var database = await CreateAsync(prdb);

        var answer = await AskAsync(
            database,
            PrdbWork.Preview,
            (client, token) => client.Videos[AVideo].UserImages.GetAsync(cancellationToken: token));

        Assert.NotNull(answer);
        Assert.Empty(answer);
        Assert.Single(prdb.AskedFor(ByVideo(AVideo)));
    }

    /// <summary>
    /// The per-Video list is the only one that can carry a link, and the fields
    /// the whole design reads come off it: the hash the preview was made from,
    /// the sprite geometry, the paired WebVTT, and the two moderation strings.
    /// </summary>
    [Fact]
    public async Task The_per_video_list_carries_the_hash_the_geometry_and_the_pair()
    {
        var prdb = new FakePrdbApi().Answers(
            ByVideo(AVideo),
            $"[{Sprite(ASprite, AVideo, "A1B2C3D4E5F60718")},{Still(AStill, AVideo, "A1B2C3D4E5F60718")}]");

        await using var database = await CreateAsync(prdb);

        var answer = await AskAsync(
            database,
            PrdbWork.Preview,
            (client, token) => client.Videos[AVideo].UserImages.GetAsync(cancellationToken: token));

        var sprite = Assert.Single(answer!, row => row.Id == ASprite);

        Assert.Equal(AVideo, sprite.VideoId);
        Assert.Equal("A1B2C3D4E5F60718", sprite.BasedOnFileWithOsHash);
        Assert.Equal("SpriteSheet", sprite.PreviewImageType);
        Assert.True(sprite.HasVtt);
        Assert.Equal("https://cdn.invalid/previews/sprite.vtt", sprite.VttUrl);
        Assert.Equal(100, sprite.SpriteTileCount);
        Assert.Equal(10, sprite.SpriteColumns);
        Assert.Equal(10, sprite.SpriteRows);
        Assert.Equal(320, sprite.SpriteTileWidth);
        Assert.Equal(180, sprite.SpriteTileHeight);

        var still = Assert.Single(answer!, row => row.Id == AStill);

        // A single image carries no geometry and no pair, which is the shape
        // the gallery has to tell apart from a sprite by more than its type.
        Assert.Equal("Single", still.PreviewImageType);
        Assert.False(still.HasVtt);
        Assert.Null(still.VttUrl);
        Assert.Null(still.SpriteTileCount);
    }

    /// <summary>
    /// The correction ADR 0061 was written around: the by-hash list answers with
    /// <em>unlinked</em> rows, so it cannot name a Video for a file however good
    /// the hash is. This is the fixture that would fail if prdb ever started
    /// including linked rows — at which point the proposal in
    /// <c>docs/prdb-api-proposals.md</c> has been answered.
    /// </summary>
    [Fact]
    public async Task The_by_hash_list_answers_with_unlinked_previews_and_names_no_video()
    {
        const string Hash = "0F1E2D3C4B5A6978";

        var prdb = new FakePrdbApi().Answers(ByHash(Hash), $"[{Unlinked(AnUnlinkedPreview, Hash)}]");

        await using var database = await CreateAsync(prdb);

        var answer = await AskAsync(
            database,
            PrdbWork.UserPreviews,
            (client, token) => client.VideoUserImages.ByOsHash[Hash].GetAsync(cancellationToken: token));

        var row = Assert.Single(answer!);

        Assert.Equal(Hash, row.BasedOnFileWithOsHash);
        Assert.Null(row.VideoId);
    }

    /// <summary>
    /// The change feed's envelope: the cursor to resume from, whether there is
    /// more, and the server's own clock — the last of which is the only lower
    /// bound prdb will read back, and the one an empty page has to be continued
    /// from.
    /// </summary>
    [Fact]
    public async Task The_change_feed_pages_by_a_cursor_and_carries_the_servers_clock()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, Page($"{{\"eventType\":\"Updated\",\"videoUserImage\":{Sprite(ASprite, AVideo, "A1B2C3D4E5F60718")}}}", hasMore: true, at: Noon))
            .Answers(Changes, Page(items: null, hasMore: false, at: Noon.AddMinutes(5)));

        await using var database = await CreateAsync(prdb);

        var first = await AskAsync(
            database,
            PrdbWork.UserPreviews,
            (client, token) => client.VideoUserImages.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = 1000;
                    request.QueryParameters.Since = DateTimeOffset.MinValue;
                },
                token));

        Assert.True(first!.HasMore);
        Assert.Equal(Noon, first.NextCursor!.UpdatedAtUtc);
        Assert.Equal(ASprite, first.NextCursor.Id);
        Assert.Equal(Noon, first.ServerTimeUtc);

        var second = await AskAsync(
            database,
            PrdbWork.UserPreviews,
            (client, token) => client.VideoUserImages.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = 1000;
                    request.QueryParameters.Since = first.NextCursor!.UpdatedAtUtc;
                    request.QueryParameters.SinceId = first.NextCursor.Id;
                },
                token));

        // An empty page carries no row to continue from, which is exactly why
        // serverTimeUtc is on the envelope rather than beside the rows.
        Assert.Empty(second!.Items!);
        Assert.False(second.HasMore);
        Assert.Equal(Noon.AddMinutes(5), second.ServerTimeUtc);

        var asked = prdb.AskedFor(Changes);

        Assert.Equal(2, asked.Count);
        Assert.Equal(Noon, Time(Query(asked[1], "Since")));
        Assert.Equal(ASprite.ToString("D"), Query(asked[1], "SinceId"));
    }

    /// <summary>
    /// A withdrawal and a restoration are the same row twice, differing in the
    /// two moderation strings — never in the id. That is what
    /// <see cref="UserPreviewModeration"/> compares, and this is the fixture it
    /// is written against.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_and_a_restoration_are_the_same_row_under_two_signatures()
    {
        var prdb = new FakePrdbApi()
            .Answers(Changes, Page(
                $"{{\"eventType\":\"Updated\",\"videoUserImage\":{Sprite(ASprite, AVideo, "A1B2C3D4E5F60718", status: "Hidden", visibility: "Private")}}}",
                hasMore: false,
                at: Noon))
            .Answers(Changes, Page(
                $"{{\"eventType\":\"Updated\",\"videoUserImage\":{Sprite(ASprite, AVideo, "A1B2C3D4E5F60718")}}}",
                hasMore: false,
                at: Noon.AddHours(1)));

        await using var database = await CreateAsync(prdb);

        var shownUnder = UserPreviewModeration.Signature("Approved", "Public");

        var withdrawal = await ChangedAsync(database);

        Assert.Equal(ASprite, withdrawal.Id);
        Assert.False(UserPreviewModeration.Shows(
            withdrawal.IsDeleted ?? false,
            UserPreviewModeration.Signature(withdrawal.ModerationStatus, withdrawal.ModerationVisibility),
            shownUnder));

        var restoration = await ChangedAsync(database);

        Assert.Equal(ASprite, restoration.Id);
        Assert.True(UserPreviewModeration.Shows(
            restoration.IsDeleted ?? false,
            UserPreviewModeration.Signature(restoration.ModerationStatus, restoration.ModerationVisibility),
            shownUnder));
    }

    /// <summary>
    /// A soft delete overrides the signature entirely: a row that is deleted is
    /// not shown even if it still calls itself approved and public.
    /// </summary>
    [Fact]
    public async Task A_soft_deleted_row_is_not_shown_whatever_it_calls_itself()
    {
        var prdb = new FakePrdbApi().Answers(Changes, Page(
            $"{{\"eventType\":\"Deleted\",\"videoUserImage\":{Sprite(ASprite, AVideo, "A1B2C3D4E5F60718", deleted: true)}}}",
            hasMore: false,
            at: Noon));

        await using var database = await CreateAsync(prdb);

        var row = await ChangedAsync(database);

        Assert.True(row.IsDeleted);
        Assert.False(UserPreviewModeration.Shows(
            row.IsDeleted ?? false,
            UserPreviewModeration.Signature(row.ModerationStatus, row.ModerationVisibility),
            UserPreviewModeration.Signature("Approved", "Public")));
    }

    /// <summary>
    /// The governor refuses this population before it refuses a feed, and the
    /// refusal arrives as ADR 0014's deferral rather than as a failure — which
    /// is what lets a routine leave every durable row exactly as it was.
    /// </summary>
    [Fact]
    public async Task A_short_budget_defers_a_user_preview_read_while_a_preview_still_goes()
    {
        // Twelve per cent of the hour left: above Preview's eight and below the
        // thirty-five this population is held back at.
        var prdb = new FakePrdbApi { Hourly = (Limit: 100, Remaining: 12, ResetInSeconds: 60) }
            .Answers(ByVideo(AVideo), "[]");

        await using var database = await CreateAsync(prdb);

        // One answered request, so that the governor has a reading at all.
        await AskAsync(
            database,
            PrdbWork.Preview,
            (client, token) => client.Videos[AVideo].UserImages.GetAsync(cancellationToken: token));

        var deferred = await Assert.ThrowsAsync<PrdbDeferredException>(() => AskAsync(
            database,
            PrdbWork.UserPreviews,
            (client, token) => client.Videos[AVideo].UserImages.GetAsync(cancellationToken: token)));

        Assert.Equal(PrdbWork.UserPreviews, deferred.Work);

        // And the interactive half of the same population still goes, which is
        // the whole point of giving it the Preview precedence.
        var still = await AskAsync(
            database,
            PrdbWork.Preview,
            (client, token) => client.Videos[AVideo].UserImages.GetAsync(cancellationToken: token));

        Assert.NotNull(still);
    }

    private static async Task<Prdb.Sdk.Generated.Models.VideoUserImageDto> ChangedAsync(TestDatabase database)
    {
        var page = await AskAsync(
            database,
            PrdbWork.UserPreviews,
            (client, token) => client.VideoUserImages.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = 1000;
                    request.QueryParameters.Since = DateTimeOffset.MinValue;
                },
                token));

        return Assert.Single(page!.Items!).VideoUserImage!;
    }

    private static async Task<TAnswer?> AskAsync<TAnswer>(
        TestDatabase database,
        PrdbWork work,
        Func<Prdb.Sdk.Generated.PrdbClient, CancellationToken, Task<TAnswer?>> ask)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<PrdbGateway>()
            .AskAsync(ApiKey, work, ask, TestContext.Current.CancellationToken);
    }

    private static Task<TestDatabase> CreateAsync(FakePrdbApi prdb) =>
        TestDatabase.CreateAsync(prdb: prdb);

    private static string? Query(Uri asked, string parameter) =>
        HttpUtility.ParseQueryString(asked.Query)[parameter];

    private static DateTimeOffset Time(string? value) => DateTimeOffset.Parse(
        value!,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind);

    private static string Page(string? items, bool hasMore, DateTimeOffset at) =>
        $$"""
        {
          "items": [{{items}}],
          "pageSize": 1000,
          "hasMore": {{(hasMore ? "true" : "false")}},
          "serverTimeUtc": "{{Stamp(at)}}",
          "nextCursor": {{(items is null ? "null" : $$"""{"updatedAtUtc":"{{Stamp(at)}}","id":"{{ASprite:D}}"}""")}}
        }
        """;

    private static string Sprite(
        Guid id,
        Guid? videoId,
        string hash,
        string status = "Approved",
        string visibility = "Public",
        bool deleted = false) =>
        $$"""
        {
          "id": "{{id:D}}",
          "userId": "{{AUser:D}}",
          "videoId": {{(videoId is { } linked ? $"\"{linked:D}\"" : "null")}},
          "basedOnFileWithOsHash": "{{hash}}",
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
          "isDeleted": {{(deleted ? "true" : "false")}},
          "createdAtUtc": "{{Stamp(Noon.AddDays(-1))}}",
          "updatedAtUtc": "{{Stamp(Noon)}}"
        }
        """;

    private static string Still(Guid id, Guid? videoId, string hash) =>
        $$"""
        {
          "id": "{{id:D}}",
          "userId": "{{AUser:D}}",
          "videoId": {{(videoId is { } linked ? $"\"{linked:D}\"" : "null")}},
          "basedOnFileWithOsHash": "{{hash}}",
          "previewImageType": "Single",
          "filesize": 91234,
          "width": 1280,
          "height": 720,
          "displayOrder": 1,
          "url": "https://cdn.invalid/previews/still.jpg",
          "hasVtt": false,
          "vttUrl": null,
          "moderationStatus": "Approved",
          "moderationVisibility": "Public",
          "isDeleted": false,
          "createdAtUtc": "{{Stamp(Noon.AddDays(-1))}}",
          "updatedAtUtc": "{{Stamp(Noon)}}"
        }
        """;

    private static string Unlinked(Guid id, string hash) =>
        $$"""
        {
          "id": "{{id:D}}",
          "userId": "{{AUser:D}}",
          "videoId": null,
          "basedOnFileWithOsHash": "{{hash}}",
          "previewImageType": "Single",
          "filesize": 91234,
          "width": 1280,
          "height": 720,
          "displayOrder": 0,
          "url": "https://cdn.invalid/previews/orphan.jpg",
          "hasVtt": false,
          "moderationStatus": "Approved",
          "moderationVisibility": "Public",
          "isDeleted": false,
          "createdAtUtc": "{{Stamp(Noon.AddDays(-1))}}",
          "updatedAtUtc": "{{Stamp(Noon)}}"
        }
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
