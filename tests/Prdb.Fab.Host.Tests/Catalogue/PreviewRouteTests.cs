using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>
/// ADR 0060's read: one Video opened over the grid it was found in, answered
/// from local rows and naming its pictures by id.
/// </summary>
public sealed class PreviewRouteTests
{
    private const string Cdn = "https://cdn.example/";

    /// <summary>
    /// The sheet shows the same facts the card does and offers the same
    /// actions, so it is the card that is answered rather than a second shape
    /// carrying the same fields.
    /// </summary>
    [Fact]
    public async Task A_preview_carries_the_card_the_grid_drew()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application, images: 3);

        var preview = await ReadAsync(client, video);

        Assert.Equal(video, preview.Video.PrdbId);
        Assert.Equal("A scene", preview.Video.Title);
        Assert.Equal("A site", preview.Video.Site);
    }

    /// <summary>
    /// ADR 0031's three numbers, which the card has no room for and which decide
    /// nothing on their own — the spread is unreadable without the count, so
    /// they travel together or not at all.
    /// </summary>
    [Fact]
    public async Task A_preview_carries_the_consensus_runtime()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application, images: 1);

        var preview = await ReadAsync(client, video);

        Assert.Equal(2_400_000, preview.DurationMs);
        Assert.Equal(12_000, preview.DurationSpreadMs);
        Assert.Equal(9, preview.DurationFileCount);
    }

    /// <summary>
    /// prdb's own order, oldest first with the id breaking ties. It is
    /// documented as stable and expressly not a ranking, so the gallery shows
    /// it as given and says nothing about which picture is best.
    /// </summary>
    [Fact]
    public async Task The_pictures_come_in_prdbs_order_with_the_chosen_one_marked()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application, images: 4);

        var preview = await ReadAsync(client, video);

        Assert.Equal(4, preview.Images.Count);
        Assert.True(preview.Images[0].Chosen);
        Assert.DoesNotContain(preview.Images.Skip(1), image => image.Chosen);
    }

    /// <summary>
    /// A picture whose URL was found dead answers 204 every time it is asked
    /// for, so sending it would be a gap in the strip rather than a picture.
    /// The Video's <em>choice</em> is still the first entry carrying a URL —
    /// ADR 0027's clause reads the array, not the cache marks — so a dead
    /// choice leaves the gallery with nothing marked.
    /// </summary>
    [Fact]
    public async Task A_dead_url_is_left_out_and_takes_the_mark_with_it()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application, images: 3, deadPosition: 0);

        var preview = await ReadAsync(client, video);

        Assert.Equal(2, preview.Images.Count);
        Assert.DoesNotContain(preview.Images, image => image.Chosen);
    }

    /// <summary>The Actors, which the card has no room for, alphabetically.</summary>
    [Fact]
    public async Task A_preview_carries_the_actors()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        var video = await SeedAsync(application, images: 1, actors: ["Zoe Last", "Ada First"]);

        var preview = await ReadAsync(client, video);

        Assert.Equal(["Ada First", "Zoe Last"], preview.Actors.Select(actor => actor.Name));
    }

    /// <summary>
    /// A Video the Catalogue does not hold. The sheet is opened from a card, so
    /// this is a stale link rather than a normal way in — and 404 is what lets
    /// the frontend say so instead of drawing an empty Preview.
    /// </summary>
    [Fact]
    public async Task A_video_the_catalogue_does_not_hold_is_not_found()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        using var answer = await client.GetAsync(
            $"/api/catalogue/videos/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    private static async Task<Answer> ReadAsync(HttpClient client, Guid video)
    {
        using var answer = await client.GetAsync(
            $"/api/catalogue/videos/{video}",
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        return (await answer.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// The wire shape, written out here rather than deserialised into the
    /// server's own record: the enums travel as names, and a test that borrowed
    /// the C# type would be asserting that the two agree by construction rather
    /// than that the document says what it should.
    /// </summary>
    private sealed record Answer(
        Card Video,
        long? DurationMs,
        long? DurationSpreadMs,
        int? DurationFileCount,
        IReadOnlyList<Credit> Actors,
        IReadOnlyList<Picture> Images);

    private sealed record Card(Guid PrdbId, string Title, string? Site, Guid? SitePrdbId);

    private sealed record Credit(Guid PrdbId, string Name);

    private sealed record Picture(Guid PrdbId, bool Chosen);

    /// <summary>One Catalogue Video with a Site, a runtime, pictures and credits.</summary>
    private static async Task<Guid> SeedAsync(
        FabApplication application,
        int images,
        int? deadPosition = null,
        string[]? actors = null)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var site = new CatalogueSiteRow { PrdbId = Guid.NewGuid(), Title = "A site" };

        context.CatalogueSites.Add(site);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var video = new CatalogueVideoRow
        {
            PrdbId = Guid.NewGuid(),
            Title = "A scene",
            NormalisedTitle = "a scene",
            SiteId = site.Id,
            DurationMs = 2_400_000,
            DurationSpreadMs = 12_000,
            DurationFileCount = 9,
        };

        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        for (var position = 0; position < images; position++)
        {
            context.CatalogueImages.Add(new CatalogueImageRow
            {
                PrdbId = Guid.NewGuid(),
                VideoId = video.Id,
                Url = $"{Cdn}{position}.png",
                Position = position,
                FoundDead = position == deadPosition,
            });
        }

        foreach (var name in actors ?? [])
        {
            var actor = new CatalogueActorRow { PrdbId = Guid.NewGuid(), Name = name };

            context.CatalogueActors.Add(actor);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            context.CatalogueVideoActors.Add(new CatalogueVideoActorRow
            {
                VideoId = video.Id,
                ActorId = actor.Id,
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return video.PrdbId;
    }
}
