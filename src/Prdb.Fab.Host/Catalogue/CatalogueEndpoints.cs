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

        // One Video for a Preview (ADR 0060), from local rows only: a person
        // opening a sheet over a grid spends no prdb request, and the grid
        // underneath is neither re-read nor re-paged.
        group.MapGet("/videos/{prdbId:guid}", async Task<Results<Ok<VideoPreview>, NotFound>> (
            Guid prdbId,
            CatalogueBrowse browse,
            CancellationToken cancellationToken) =>
        {
            var preview = await browse.VideoAsync(prdbId, cancellationToken);
            return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
        });

        // ADR 0060's one request. A person opened a Preview of a Video the
        // Catalogue holds no picture of; this is the act, which is why it is a
        // POST and not a side effect of the read above.
        group.MapPost("/videos/{prdbId:guid}/pictures", async Task<Results<Ok<PreviewPictureAsk>, NotFound>> (
            Guid prdbId,
            PreviewPictures pictures,
            CancellationToken cancellationToken) =>
        {
            var ask = await pictures.AskAsync(prdbId, cancellationToken);
            return ask.Outcome == PreviewPictureOutcome.VideoNotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(ask);
        });

        // ADR 0061's demand, and the same shape for the same reason: a GET that
        // scheduled work would make ADR 0018's rule — refreshing never causes
        // work — one the code contradicts where it is easiest to read. One
        // request per Video opened, deduplicated by the routine row behind it.
        group.MapPost("/videos/{prdbId:guid}/user-previews", async (
            Guid prdbId,
            UserPreviews previews,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await previews.AskAsync(
                prdbId,
                UserPreviewDemand.Preview,
                cancellationToken)));

        // What a sprite sheet's tiles are, read off the cached pair. A local
        // read that fills the cache if it has to: the fetch is against a CDN
        // and spends no prdb budget (ADR 0030), and the gallery cannot draw a
        // scrubbing strip without knowing which tile is when.
        group.MapGet("/user-previews/{previewId:guid}/{version}/tiles", async (
            Guid previewId,
            string version,
            PreviewAssetCache previews,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await previews.TilesAsync(previewId, version, cancellationToken)));

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
