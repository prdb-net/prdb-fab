using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>The two catalogue-only browse surfaces deferred until Release discovery.</summary>
public sealed class SitesAndActorsRouteTests
{
    [Fact]
    public async Task Sites_and_actors_are_searched_locally_and_open_their_video_grids()
    {
        var prdb = new RefusesEverything();
        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        var sites = await client.GetFromJsonAsync<SitePage>(
            "/api/catalogue/sites?search=North&page=1",
            TestContext.Current.CancellationToken);
        var actors = await client.GetFromJsonAsync<ActorPage>(
            "/api/catalogue/actors?search=Mira&page=1",
            TestContext.Current.CancellationToken);
        var site = await client.GetFromJsonAsync<SiteVideos>(
            $"/api/catalogue/sites/{seeded.SiteId}?search=Second&page=1",
            TestContext.Current.CancellationToken);
        var actor = await client.GetFromJsonAsync<ActorVideos>(
            $"/api/catalogue/actors/{seeded.ActorId}?page=1",
            TestContext.Current.CancellationToken);
        var videos = await client.GetFromJsonAsync<VideoPage>(
            "/api/catalogue/videos?search=Second&page=1",
            TestContext.Current.CancellationToken);

        Assert.NotNull(sites);
        var northline = Assert.Single(sites.Sites);
        Assert.Equal("Northline", northline.Title);
        Assert.Equal(1, northline.HeldVideoCount);
        Assert.NotNull(actors);
        Assert.Equal("Mira Vance", Assert.Single(actors.Actors).Name);
        var allSites = await client.GetFromJsonAsync<SitePage>(
            "/api/catalogue/sites?scope=all",
            TestContext.Current.CancellationToken);
        Assert.Equal(["Northline", "Blue Harbour"], allSites!.Sites.Select(item => item.Title));
        var heldSites = await client.GetFromJsonAsync<SitePage>(
            "/api/catalogue/sites?scope=all&held=true",
            TestContext.Current.CancellationToken);
        Assert.Equal("Northline", Assert.Single(heldSites!.Sites).Title);

        Assert.NotNull(site);
        Assert.Equal("Northline", site.Site.Title);
        Assert.Equal("Second Shift", Assert.Single(site.Videos.Videos).Title);

        Assert.NotNull(actor);
        Assert.Equal("Mira Vance", actor.Actor.Title);
        Assert.Equal(["First Light", "Second Shift"], actor.Videos.Videos.Select(video => video.Title));
        Assert.Equal("Second Shift", Assert.Single(videos!.Videos).Title);
        Assert.Equal(0, prdb.Requests);
    }

    [Fact]
    public async Task The_new_browse_routes_are_behind_the_password()
    {
        await using var application = new FabApplication();
        using var client = application.CreateClient();

        using var sites = await client.GetAsync("/api/catalogue/sites", TestContext.Current.CancellationToken);
        using var actors = await client.GetAsync("/api/catalogue/actors", TestContext.Current.CancellationToken);
        using var videos = await client.GetAsync("/api/catalogue/videos", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, sites.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, actors.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, videos.StatusCode);
    }

    private static async Task<Seeded> SeedAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var site = new CatalogueSiteRow { PrdbId = Guid.NewGuid(), Title = "Northline" };
        var otherSite = new CatalogueSiteRow { PrdbId = Guid.NewGuid(), Title = "Blue Harbour" };
        var actor = new CatalogueActorRow { PrdbId = Guid.NewGuid(), Name = "Mira Vance" };
        context.AddRange(site, otherSite, actor);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = Video("First Light", site.Id, 1);
        var second = Video("Second Shift", site.Id, 2);
        var other = Video("Harbour Lights", otherSite.Id, 3);
        context.AddRange(first, second, other);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.CatalogueVideoActors.AddRange(
            new CatalogueVideoActorRow { VideoId = first.Id, ActorId = actor.Id },
            new CatalogueVideoActorRow { VideoId = second.Id, ActorId = actor.Id });
        context.FavouriteSites.Add(new FavouriteSiteRow { SiteId = site.Id });
        context.FavouriteActors.Add(new FavouriteActorRow { ActorId = actor.Id });
        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = first.PrdbId,
            EntryDirectory = "/library/Northline/First Light",
            FiledAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
        });
        context.VideoFiles.Add(new VideoFileRow
        {
            Id = Guid.NewGuid(),
            LibraryEntryVideoId = first.PrdbId,
            FiledPath = "/library/Northline/First Light/First Light.mkv",
            QualityLabel = "1080p",
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new(site.PrdbId, actor.PrdbId);
    }

    private static CatalogueVideoRow Video(string title, long siteId, int day) => new()
    {
        PrdbId = Guid.NewGuid(),
        Title = title,
        NormalisedTitle = title.ToLowerInvariant(),
        SiteId = siteId,
        CreatedAtUtc = new DateTimeOffset(2026, 8, day, 12, 0, 0, TimeSpan.Zero),
        UpdatedAtUtc = new DateTimeOffset(2026, 8, day, 12, 0, 0, TimeSpan.Zero),
    };

    private sealed record Seeded(Guid SiteId, Guid ActorId);
    private sealed record SiteCard(Guid PrdbId, string Title, int VideoCount, int HeldVideoCount);
    private sealed record ActorCard(Guid PrdbId, string Name, int VideoCount);
    private sealed record VideoCard(Guid PrdbId, string Title);
    private sealed record VideoPage(IReadOnlyList<VideoCard> Videos, int Page, int Total);
    private sealed record BrowseContext(Guid PrdbId, string Title);
    private sealed record SitePage(IReadOnlyList<SiteCard> Sites, int Page, int Total);
    private sealed record ActorPage(IReadOnlyList<ActorCard> Actors, int Page, int Total);
    private sealed record SiteVideos(BrowseContext Site, VideoPage Videos);
    private sealed record ActorVideos(BrowseContext Actor, VideoPage Videos);

    private sealed class RefusesEverything : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            throw new HttpRequestException("A browse read must not reach prdb.");
        }
    }
}
