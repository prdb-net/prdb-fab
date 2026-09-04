using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Fab.Infrastructure.Acquisition;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Prdb.Fab.Host.Catalogue;

/// <summary>
/// The browse surfaces. ADR 0012 makes five of them and this slice builds two:
/// What's New, which ADR 0013 calls the landing page and which is what the
/// catalogue exists for, and the wanted list, which is where onboarding ends.
/// </summary>
/// <remarks>
/// ADR 0040's shape, and ADR 0036's rule about the address: the page is a query
/// parameter because it is what a person would link to, and it is counted from
/// one because that is what they would read.
/// </remarks>
public static class CatalogueEndpoints
{
    public static void MapCatalogue(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/catalogue").WithTags("Catalogue");

        group.MapGet("/whats-new", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            int page = 1) =>
            TypedResults.Ok(await browse.WhatsNewAsync(page, cancellationToken)));
        group.MapPost("/whats-new/observed", async (
            WhatsNewObservation observation,
            CatalogueBrowse browse,
            CancellationToken cancellationToken) =>
        {
            await browse.ObserveWhatsNewAsync(
                observation.VideoId,
                observation.CreatedAt,
                cancellationToken);
            return TypedResults.Ok();
        });

        // The locally projected account list, including accepted catalogue
        // actions and a manual acquisition's durable pending intent (ADR 0048).
        group.MapGet("/wanted", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            int page = 1) =>
            TypedResults.Ok(await browse.WantedAsync(page, cancellationToken)));

        group.MapGet("/videos", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            string? search = null,
            int page = 1,
            CatalogueVideoFilter filter = CatalogueVideoFilter.Available,
            CatalogueVideoSort sort = CatalogueVideoSort.ReleaseDateDescending) =>
            TypedResults.Ok(await browse.VideosAsync(search, page, filter, sort, cancellationToken)));

        MapPreference(group, "/wanted/{prdbId:guid}", AccountPreferenceKind.WantedVideo);
        MapPreference(group, "/actors/{prdbId:guid}/favourite", AccountPreferenceKind.FavouriteActor);
        MapPreference(group, "/sites/{prdbId:guid}/favourite", AccountPreferenceKind.FavouriteSite);
        group.MapPost("/videos/{prdbId:guid}/download-best", async Task<Results<Ok<DownloadVerdict>, NotFound>> (
            Guid prdbId,
            PersonDownloads downloads,
            CancellationToken cancellationToken) =>
        {
            var verdict = await downloads.DownloadBestAsync(prdbId, cancellationToken);
            return verdict is null ? TypedResults.NotFound() : TypedResults.Ok(verdict);
        });

        group.MapGet("/sites", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            string? search = null,
            int page = 1,
            string? scope = null,
            bool held = false) =>
            TypedResults.Ok(await browse.SitesAsync(search, page, ScopeOf(scope), held, cancellationToken)));

        group.MapGet("/sites/{prdbId:guid}", ReadSiteAsync);

        group.MapGet("/actors", async (
            CatalogueBrowse browse,
            CancellationToken cancellationToken,
            string? search = null,
            int page = 1,
            string? scope = null) =>
            TypedResults.Ok(await browse.ActorsAsync(search, page, ScopeOf(scope), cancellationToken)));

        group.MapGet("/actors/{prdbId:guid}", ReadActorAsync);
        group.MapPost("/actors/{prdbId:guid}/latest-videos", async Task<Results<Ok<ActorVideoLoadStart>, NotFound>> (
            Guid prdbId,
            ActorVideoLoads loads,
            CancellationToken cancellationToken) =>
        {
            var answer = await loads.StartAsync(prdbId, cancellationToken);
            return answer.Outcome == ActorVideoLoadStartOutcome.ActorNotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(answer);
        });
    }

    private static CatalogueScope ScopeOf(string? scope) =>
        string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
            ? CatalogueScope.All
            : CatalogueScope.Favourites;

    private static void MapPreference(
        RouteGroupBuilder group,
        string pattern,
        AccountPreferenceKind kind)
    {
        group.MapPost(pattern, async (
            Guid prdbId,
            AccountPreferences preferences,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await preferences.SetAsync(kind, prdbId, desired: true, cancellationToken)));
        group.MapDelete(pattern, async (
            Guid prdbId,
            AccountPreferences preferences,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await preferences.SetAsync(kind, prdbId, desired: false, cancellationToken)));
    }

    private static async Task<Results<Ok<SiteVideos>, NotFound>> ReadSiteAsync(
        Guid prdbId,
        CatalogueBrowse browse,
        CancellationToken cancellationToken,
        string? search = null,
        int page = 1)
    {
        var answer = await browse.SiteAsync(prdbId, search, page, cancellationToken);
        return answer is null ? TypedResults.NotFound() : TypedResults.Ok(answer);
    }

    private static async Task<Results<Ok<ActorVideos>, NotFound>> ReadActorAsync(
        Guid prdbId,
        CatalogueBrowse browse,
        CancellationToken cancellationToken,
        string? search = null,
        int page = 1)
    {
        var answer = await browse.ActorAsync(prdbId, search, page, cancellationToken);
        return answer is null ? TypedResults.NotFound() : TypedResults.Ok(answer);
    }
}

public sealed record WhatsNewObservation(long VideoId, DateTimeOffset CreatedAt);
