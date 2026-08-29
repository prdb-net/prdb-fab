using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

public sealed class AccountPreferenceRouteTests
{
    [Fact]
    public async Task Catalogue_preferences_write_prdb_then_project_locally_and_delete_is_idempotent()
    {
        var prdb = new PreferenceApi();
        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);
        using var client = await application.SignedInClientAsync();
        var ids = await SeedAsync(application);

        Assert.Equal("Updated", (await WriteAsync(client, $"/api/catalogue/wanted/{ids.Video}")).Outcome);
        Assert.Equal("Updated", (await WriteAsync(client, $"/api/catalogue/actors/{ids.Actor}/favourite")).Outcome);
        Assert.Equal("Updated", (await WriteAsync(client, $"/api/catalogue/sites/{ids.Site}/favourite")).Outcome);

        using var removed = await client.DeleteAsync(
            $"/api/catalogue/wanted/{ids.Video}",
            TestContext.Current.CancellationToken);
        removed.EnsureSuccessStatusCode();
        Assert.Equal("Updated", (await removed.Content.ReadFromJsonAsync<Verdict>(
            TestContext.Current.CancellationToken))!.Outcome);

        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.False(await context.WantedVideos.AnyAsync(TestContext.Current.CancellationToken));
        Assert.True(await context.FavouriteActors.AnyAsync(TestContext.Current.CancellationToken));
        Assert.True(await context.FavouriteSites.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(4, prdb.Writes.Count);
    }

    [Fact]
    public async Task A_failed_remote_write_leaves_the_previous_local_state_unchanged()
    {
        var prdb = new PreferenceApi { PostStatus = HttpStatusCode.ServiceUnavailable };
        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);
        using var client = await application.SignedInClientAsync();
        var ids = await SeedAsync(application);

        var verdict = await WriteAsync(client, $"/api/catalogue/actors/{ids.Actor}/favourite");
        Assert.Equal("Failed", verdict.Outcome);

        await using var scope = application.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .FavouriteActors.AnyAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Ids> SeedAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.PrdbApiKey, "fixture"),
            TestContext.Current.CancellationToken);
        var video = new CatalogueVideoRow { PrdbId = Guid.NewGuid(), Title = "Video" };
        var actor = new CatalogueActorRow { PrdbId = Guid.NewGuid(), Name = "Actor" };
        var site = new CatalogueSiteRow { PrdbId = Guid.NewGuid(), Title = "Site" };
        context.AddRange(video, actor, site);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(video.PrdbId, actor.PrdbId, site.PrdbId);
    }

    private static async Task<Verdict> WriteAsync(HttpClient client, string path)
    {
        using var response = await client.PostAsync(path, null, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private sealed class PreferenceApi : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Writes { get; } = [];
        public HttpStatusCode PostStatus { get; set; } = HttpStatusCode.NoContent;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Writes.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(new HttpResponseMessage(
                request.Method == HttpMethod.Delete ? HttpStatusCode.NotFound : PostStatus));
        }
    }

    private sealed record Ids(Guid Video, Guid Actor, Guid Site);
    private sealed record Verdict(string Outcome, bool Desired, string Detail);
}
