using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>
/// ADR 0061 through the API: what a sheet is told about the pictures other
/// people made, what asking for them costs, and what a withdrawal does to a
/// browser that is already holding one.
/// </summary>
public sealed class UserPreviewRouteTests
{
    private const string Hash = "A1B2C3D4E5F60718";

    /// <summary>
    /// The read carries the sprites and the singles apart, each named by id and
    /// version, and never by the address prdb published. ADR 0030's rule — the
    /// browser asks the tool and never the CDN — is what makes a withdrawal
    /// enforceable rather than advisory, and it matters more here than for an
    /// image, because this population is moderated.
    /// </summary>
    [Fact]
    public async Task A_preview_names_its_user_previews_by_id_and_version_and_no_url()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application);
        var preview = await GiveAsync(application, video, sprite: true);

        var answer = await ReadRawAsync(client, video);

        Assert.DoesNotContain("cdn.example", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", answer, StringComparison.OrdinalIgnoreCase);

        var read = await ReadAsync(client, video);
        var card = Assert.Single(read.UserPreviews);

        Assert.Equal(preview, card.PrdbId);
        Assert.True(card.Sprite);
        Assert.False(string.IsNullOrWhiteSpace(card.Version));
    }

    /// <summary>
    /// A preview prdb has stopped showing is not in the answer at all, and a
    /// kind this build does not know is not either. A gallery is a strip of
    /// pictures rather than a census of rows.
    /// </summary>
    [Fact]
    public async Task A_withdrawn_or_unknown_preview_is_not_in_the_answer()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application);
        var shown = await GiveAsync(application, video, sprite: false);
        await GiveAsync(application, video, sprite: false, shown: false);
        await GiveAsync(application, video, sprite: false, kind: "AnimatedThing");

        var read = await ReadAsync(client, video);

        Assert.Equal(shown, Assert.Single(read.UserPreviews).PrdbId);
    }

    /// <summary>
    /// ADR 0018, on the surface where it is easiest to break: reading a Preview
    /// over and over registers no interest and schedules no work. Only the POST
    /// does, and it is deduplicated behind one routine row.
    /// </summary>
    [Fact]
    public async Task Reading_costs_nothing_and_asking_twice_schedules_once()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application);

        await ReadAsync(client, video);
        await ReadAsync(client, video);

        Assert.Equal(0, await InterestsAsync(application));

        await AskAsync(client, video);
        await AskAsync(client, video);
        await AskAsync(client, video);

        Assert.Equal(1, await InterestsAsync(application));
        Assert.Equal(1, await ScheduledAsync(application));
    }

    /// <summary>
    /// Walking a page of cards with the arrow keys is one ask per Video and one
    /// scheduled read per Video — never one per glance. The sheet says which
    /// Video it is about, so this is the request count a person moving across a
    /// grid actually produces.
    /// </summary>
    [Fact]
    public async Task Moving_across_videos_asks_once_for_each()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var videos = new List<Guid>();

        for (var index = 0; index < 5; index++)
        {
            videos.Add(await SeedAsync(application, $"Scene {index}"));
        }

        // There and back again, which is what stepping with the arrow keys and
        // stepping back does.
        foreach (var video in videos.Concat(Enumerable.Reverse(videos)))
        {
            await ReadAsync(client, video);
            await AskAsync(client, video);
        }

        Assert.Equal(5, await InterestsAsync(application));
        Assert.Equal(5, await ScheduledAsync(application));
    }

    /// <summary>
    /// The bytes route: same-origin, addressed by version, and answering
    /// nothing for a version that is not the current one rather than quietly
    /// answering with the current one.
    /// </summary>
    [Fact]
    public async Task A_version_that_is_not_the_current_one_is_answered_with_nothing()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application);
        var preview = await GiveAsync(application, video, sprite: true);

        using var answer = await client.GetAsync(
            $"/api/previews/{preview}/000000000000/image",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, answer.StatusCode);
    }

    /// <summary>
    /// A user preview may be withdrawn at any time, so the answer is cacheable
    /// for minutes rather than the year an <c>images[]</c> picture gets. A
    /// horizon outliving a withdrawal would make revocation something the
    /// server believes and the browser ignores.
    /// </summary>
    [Fact]
    public async Task A_user_preview_is_never_cacheable_for_a_year()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application);
        var preview = await GiveAsync(application, video, sprite: true);

        using var answer = await client.GetAsync(
            $"/api/previews/{preview}/{await VersionAsync(application, preview)}/image",
            TestContext.Current.CancellationToken);

        var caching = answer.Headers.CacheControl!.ToString();

        Assert.Contains("private", caching, StringComparison.Ordinal);
        Assert.DoesNotContain("immutable", caching, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(5), answer.Headers.CacheControl.MaxAge);
    }

    /// <summary>
    /// The tiles route answers a verdict rather than failing: a preview whose
    /// pair cannot be fetched is one the strip is not drawn for, and the
    /// gallery is told whether it is worth asking again.
    /// </summary>
    [Fact]
    public async Task The_tiles_route_answers_a_verdict_for_a_pair_it_cannot_fetch()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application);
        var preview = await GiveAsync(application, video, sprite: true);

        using var answer = await client.GetAsync(
            $"/api/catalogue/user-previews/{preview}/{await VersionAsync(application, preview)}/tiles",
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        var tiles = await answer.Content.ReadFromJsonAsync<Strip>(TestContext.Current.CancellationToken);

        Assert.False(tiles!.Usable);
        Assert.Empty(tiles.Tiles);
    }

    private sealed record Strip(bool Usable, IReadOnlyList<object> Tiles, bool Coming);

    private sealed record Answer(IReadOnlyList<Card> UserPreviews, bool UserPreviewsComing);

    private sealed record Card(Guid PrdbId, string Version, bool Sprite);

    private static async Task<Answer> ReadAsync(HttpClient client, Guid video)
    {
        using var answer = await client.GetAsync(
            $"/api/catalogue/videos/{video}",
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        return (await answer.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<string> ReadRawAsync(HttpClient client, Guid video) =>
        await client.GetStringAsync(
            $"/api/catalogue/videos/{video}",
            TestContext.Current.CancellationToken);

    private static async Task AskAsync(HttpClient client, Guid video)
    {
        using var answer = await client.PostAsync(
            $"/api/catalogue/videos/{video}/user-previews",
            content: null,
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();
    }

    private static async Task<int> InterestsAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .UserPreviewInterests.CountAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int> ScheduledAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Routines.CountAsync(
                row => UserPreviewReadRoutine.Names.Contains(row.Name),
                TestContext.Current.CancellationToken);
    }

    private static async Task<string> VersionAsync(FabApplication application, Guid preview)
    {
        await using var scope = application.Services.CreateAsyncScope();

        var row = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .UserPreviews.AsNoTracking()
            .SingleAsync(entry => entry.PrdbId == preview, TestContext.Current.CancellationToken);

        return PreviewAssetCache.VersionOf(row);
    }

    private static async Task<Guid> SeedAsync(FabApplication application, string title = "A scene")
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = new CatalogueVideoRow
        {
            PrdbId = Guid.NewGuid(),
            Title = title,
            NormalisedTitle = title.ToLowerInvariant(),
        };

        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return video.PrdbId;
    }

    private static async Task<Guid> GiveAsync(
        FabApplication application,
        Guid video,
        bool sprite,
        bool shown = true,
        string? kind = null)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var id = Guid.NewGuid();

        context.UserPreviews.Add(new UserPreviewRow
        {
            PrdbId = id,
            VideoPrdbId = video,
            OsHash = Hash,
            Kind = kind ?? (sprite ? UserPreviewAsset.SpriteSheet : UserPreviewAsset.Single),
            Url = $"https://cdn.example/{id:n}.jpg",
            VttUrl = sprite ? $"https://cdn.example/{id:n}.vtt" : null,
            Width = 3200,
            Height = 1800,
            TileWidth = sprite ? 320 : null,
            TileHeight = sprite ? 180 : null,
            Columns = sprite ? 10 : null,
            Rows = sprite ? 10 : null,
            TileCount = sprite ? 100 : null,
            ShownUnder = UserPreviewModeration.Signature("Approved", "Public"),
            Shown = shown,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return id;
    }
}
