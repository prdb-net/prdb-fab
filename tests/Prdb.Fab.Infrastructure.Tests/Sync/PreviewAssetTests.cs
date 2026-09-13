using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0061's asset cache: a pair fetched, validated and committed together, or
/// not at all.
/// </summary>
/// <remarks>
/// ADR 0042 replaces the network at the socket, so what sits above the fake CDN
/// is the real artwork transport with its real timeout and redirect rule, the
/// real ceilings, and the real validator.
/// </remarks>
public sealed class PreviewAssetTests
{
    private static readonly Guid Video = Guid.Parse("aaaa1111-0000-4000-8000-000000000001");
    private static readonly Guid Sprite = Guid.Parse("bbbb1111-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private const string SpriteUrl = "https://cdn.example/sprite.jpg";
    private const string VttUrl = "https://cdn.example/sprite.vtt";

    private const string Vtt = """
        WEBVTT

        00:00:00.000 --> 00:00:10.000
        sprite.jpg#xywh=0,0,320,180

        00:00:10.000 --> 00:00:20.000
        sprite.jpg#xywh=320,0,320,180
        """;

    /// <summary>
    /// The whole path: both halves fetched, the WebVTT read against the sheet
    /// the image actually is, both files on disk, and only then a version
    /// committed.
    /// </summary>
    [Fact]
    public async Task A_pair_is_committed_once_both_halves_hold_together()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);
        var version = PreviewAssetCache.VersionOf(row);

        var answer = await ServeAsync(database, version, vtt: false);

        Assert.NotNull(answer.Served);
        Assert.Equal("image/jpeg", answer.Served!.MediaType);
        await answer.Served.Bytes.DisposeAsync();

        Assert.Equal(version, await CachedVersionAsync(database));
        Assert.True(Store(database).Holds(Sprite, version, paired: true));

        // And the timeline is read off the files rather than out of a column.
        await using var scope = database.Scope();
        var timeline = await scope.ServiceProvider.GetRequiredService<PreviewAssetCache>()
            .TimelineAsync(Sprite, version, TestContext.Current.CancellationToken);

        Assert.True(timeline.Usable);
        Assert.Equal(2, timeline.Tiles.Count);
    }

    /// <summary>
    /// Half a pair is nothing. A sprite whose WebVTT does not arrive is not
    /// committed, is not served, and leaves no version anybody could ask for —
    /// so there is no window in which a sheet is shown against a missing grid.
    /// </summary>
    [Fact]
    public async Task A_pair_whose_second_half_does_not_arrive_is_not_committed()
    {
        var cdn = new FakeCdn().ServesSprite(SpriteUrl, 3200, 1800);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);
        var version = PreviewAssetCache.VersionOf(row);

        var answer = await ServeAsync(database, version, vtt: false);

        Assert.Null(answer.Served);
        Assert.False(answer.AbsenceStands);
        Assert.Null(await CachedVersionAsync(database));
    }

    /// <summary>
    /// A WebVTT that does not describe the sheet — here a rectangle off the
    /// right-hand edge of the real image, which the payload's own width would
    /// have allowed — is refused, and nothing is committed.
    /// </summary>
    [Fact]
    public async Task A_webvtt_that_does_not_describe_the_real_sheet_is_refused()
    {
        var cdn = new FakeCdn()
            // The row says 3200x1800; the file is a third of that.
            .ServesSprite(SpriteUrl, 1024, 576)
            // A tile in the last column of the grid the row described: inside
            // the declared sheet, off the edge of the real one.
            .ServesText(VttUrl, """
                WEBVTT

                00:00:00.000 --> 00:00:10.000
                sprite.jpg#xywh=2880,1620,320,180
                """);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);

        var answer = await ServeAsync(database, PreviewAssetCache.VersionOf(row), vtt: false);

        Assert.Null(answer.Served);
        Assert.Null(await CachedVersionAsync(database));
    }

    /// <summary>
    /// The content check, on the half that is not an image: an error page served
    /// with a 200 is not a WebVTT, and the pair does not complete.
    /// </summary>
    [Fact]
    public async Task Something_that_is_not_a_webvtt_is_refused()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800)
            .ServesText(VttUrl, "<html><body>Not found</body></html>", "text/html");

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);

        Assert.Null((await ServeAsync(database, PreviewAssetCache.VersionOf(row), vtt: false)).Served);
        Assert.Null(await CachedVersionAsync(database));
    }

    /// <summary>
    /// The ceilings, which stand in the governor's place because none of this
    /// passes it.
    /// </summary>
    [Fact]
    public async Task An_asset_over_its_ceiling_is_refused()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800, bytes: (int)UserPreviewContract.ASprite + 1)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);

        Assert.Null((await ServeAsync(database, PreviewAssetCache.VersionOf(row), vtt: false)).Served);
        Assert.Null(await CachedVersionAsync(database));
    }

    /// <summary>
    /// A version that is not the current one is answered as absent rather than
    /// with the current one. An address that quietly means something else is the
    /// thing putting the version in it exists to prevent.
    /// </summary>
    [Fact]
    public async Task A_stale_version_is_not_answered_with_the_current_one()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        await GiveAsync(database);

        var answer = await ServeAsync(database, "000000000000", vtt: false);

        Assert.Null(answer.Served);
        Assert.True(answer.AbsenceStands);
        Assert.Empty(cdn.Asked);
    }

    /// <summary>
    /// Two readers of one preview do not fight: they fetch the same version
    /// into the same two names and both write the same value. The second read
    /// is served from the disk.
    /// </summary>
    [Fact]
    public async Task Two_readers_of_one_preview_leave_one_committed_version()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);
        var version = PreviewAssetCache.VersionOf(row);

        var first = ServeAsync(database, version, vtt: false);
        var second = ServeAsync(database, version, vtt: true);

        foreach (var answer in await Task.WhenAll(first, second))
        {
            Assert.NotNull(answer.Served);
            await answer.Served!.Bytes.DisposeAsync();
        }

        Assert.Equal(version, await CachedVersionAsync(database));

        var again = await ServeAsync(database, version, vtt: false);

        Assert.NotNull(again.Served);
        await again.Served!.Bytes.DisposeAsync();

        // Two fetches of each half at the very most — the two concurrent reads
        // — and nothing after that.
        Assert.True(cdn.Asked.Count(url => url == SpriteUrl) <= 2);
    }

    /// <summary>
    /// A withdrawal stops the serving immediately and the sweep takes the bytes;
    /// a restoration fetches them again under the same id. This is the whole
    /// reason the cache is keyed by a version rather than marked dead once.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_stops_the_serving_and_a_restoration_fetches_again()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);
        var version = PreviewAssetCache.VersionOf(row);

        (await ServeAsync(database, version, vtt: false)).Served!.Bytes.Dispose();

        await ShowAsync(database, shown: false);

        var withdrawn = await ServeAsync(database, version, vtt: false);

        Assert.Null(withdrawn.Served);
        Assert.True(withdrawn.AbsenceStands);

        var sweep = await SweepAsync(database);

        Assert.Equal(1, sweep.Orphans);
        Assert.False(Store(database).Holds(Sprite, version, paired: true));

        await ShowAsync(database, shown: true);

        var restored = await ServeAsync(database, version, vtt: false);

        Assert.NotNull(restored.Served);
        await restored.Served!.Bytes.DisposeAsync();
        Assert.True(Store(database).Holds(Sprite, version, paired: true));
    }

    /// <summary>
    /// A republished sprite is a different version. The old pair stops being
    /// claimed the moment the row changes, and the sweep finds it — which is
    /// what keeps two versions of one preview from accumulating.
    /// </summary>
    [Fact]
    public async Task A_republished_sprite_leaves_its_old_version_for_the_sweep()
    {
        const string MovedUrl = "https://cdn.example/sprite-2.jpg";

        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800)
            .ServesSprite(MovedUrl, 3200, 1800)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);
        var first = PreviewAssetCache.VersionOf(row);

        (await ServeAsync(database, first, vtt: false)).Served!.Bytes.Dispose();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            await context.UserPreviews
                .Where(preview => preview.PrdbId == Sprite)
                .ExecuteUpdateAsync(
                    preview => preview
                        .SetProperty(moved => moved.Url, MovedUrl)
                        .SetProperty(moved => moved.CachedVersion, (string?)null),
                    TestContext.Current.CancellationToken);
        }

        var second = PreviewAssetCache.VersionOf(await RowAsync(database));

        Assert.NotEqual(first, second);

        (await ServeAsync(database, second, vtt: false)).Served!.Bytes.Dispose();

        var sweep = await SweepAsync(database);

        Assert.Equal(1, sweep.Orphans);
        Assert.False(Store(database).Holds(Sprite, first, paired: true));
        Assert.True(Store(database).Holds(Sprite, second, paired: true));
    }

    /// <summary>
    /// The ceiling, held least-recently-served first, and nothing in this cache
    /// is pinned — a Video the Library holds does not put its previews outside
    /// it.
    /// </summary>
    [Fact]
    public async Task The_ceiling_is_held_and_nothing_is_pinned()
    {
        var cdn = new FakeCdn()
            .ServesSprite(SpriteUrl, 3200, 1800, bytes: 8192)
            .ServesText(VttUrl, Vtt);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database);
        var version = PreviewAssetCache.VersionOf(row);

        (await ServeAsync(database, version, vtt: false)).Served!.Bytes.Dispose();

        var sweep = await SweepAsync(database, ceiling: 16);

        Assert.Equal(1, sweep.Evicted);
        Assert.False(Store(database).Holds(Sprite, version, paired: true));
        Assert.Null(await CachedVersionAsync(database));

        // And it comes straight back when somebody looks at it again.
        var again = await ServeAsync(database, version, vtt: false);

        Assert.NotNull(again.Served);
        await again.Served!.Bytes.DisposeAsync();
    }

    /// <summary>
    /// A kind this build does not know is not shown, and no byte is fetched for
    /// it.
    /// </summary>
    [Fact]
    public async Task An_unknown_preview_type_fetches_nothing()
    {
        var cdn = new FakeCdn().ServesSprite(SpriteUrl, 3200, 1800);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database, kind: "AnimatedThing");

        var answer = await ServeAsync(database, PreviewAssetCache.VersionOf(row), vtt: false);

        Assert.Null(answer.Served);
        Assert.True(answer.AbsenceStands);
        Assert.Empty(cdn.Asked);
    }

    /// <summary>A single picture has no WebVTT half, and asking for one is absent.</summary>
    [Fact]
    public async Task A_single_picture_has_no_second_half()
    {
        var cdn = new FakeCdn().ServesSprite(SpriteUrl, 1280, 720);

        await using var database = await CreateAsync(cdn);
        var row = await GiveAsync(database, kind: UserPreviewAsset.Single, vttUrl: null);
        var version = PreviewAssetCache.VersionOf(row);

        var image = await ServeAsync(database, version, vtt: false);

        Assert.NotNull(image.Served);
        await image.Served!.Bytes.DisposeAsync();

        Assert.Null((await ServeAsync(database, version, vtt: true)).Served);
    }

    private static async Task<PreviewAssetAnswer> ServeAsync(
        TestDatabase database,
        string version,
        bool vtt)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PreviewAssetCache>()
            .ServeAsync(Sprite, version, vtt, TestContext.Current.CancellationToken);
    }

    private static async Task<PreviewAssetSweep> SweepAsync(
        TestDatabase database,
        long ceiling = UserPreviewContract.CacheBytes)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PreviewAssetEviction>()
            .SweepAsync(ceiling, TestContext.Current.CancellationToken);
    }

    private static async Task<UserPreviewRow> RowAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .UserPreviews.AsNoTracking()
            .SingleAsync(row => row.PrdbId == Sprite, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> CachedVersionAsync(TestDatabase database) =>
        (await RowAsync(database)).CachedVersion;

    private static async Task ShowAsync(TestDatabase database, bool shown)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .UserPreviews
            .Where(row => row.PrdbId == Sprite)
            .ExecuteUpdateAsync(
                row => row.SetProperty(preview => preview.Shown, shown),
                TestContext.Current.CancellationToken);
    }

    private static async Task<UserPreviewRow> GiveAsync(
        TestDatabase database,
        string kind = UserPreviewAsset.SpriteSheet,
        string? vttUrl = VttUrl)
    {
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.UserPreviews.Add(new UserPreviewRow
            {
                PrdbId = Sprite,
                VideoPrdbId = Video,
                OsHash = "A1B2C3D4E5F60718",
                Kind = kind,
                Url = SpriteUrl,
                VttUrl = vttUrl,
                Filesize = 4096,
                Width = 3200,
                Height = 1800,
                DisplayOrder = 0,
                TileCount = 100,
                TileWidth = 320,
                TileHeight = 180,
                Columns = 10,
                Rows = 10,
                ShownUnder = UserPreviewModeration.Signature("Approved", "Public"),
                Shown = true,
                UpdatedAtUtc = Noon,
                CreatedAtUtc = Noon,
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return await RowAsync(database);
    }

    private static PreviewAssetStore Store(TestDatabase database) =>
        database.Services.CreateScope().ServiceProvider.GetRequiredService<PreviewAssetStore>();

    private static Task<TestDatabase> CreateAsync(FakeCdn cdn) =>
        TestDatabase.CreateAsync(also: services =>
        {
            services.AddFabSync();

            services.AddHttpClient(FabTransports.Artwork)
                .ConfigurePrimaryHttpMessageHandler(() => cdn);
        });
}
