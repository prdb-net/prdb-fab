using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Host.ReleaseDiscovery;

/// <summary>The one local Release table, reached through one browse context.</summary>
public static class ReleaseEndpoints
{
    public static void MapReleaseDiscovery(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/releases", ReadAsync).WithTags("Release discovery");

        routes.MapGet(
            "/api/releases/discovery-routines",
            async (ReleaseDiscoveryControls controls, CancellationToken cancellationToken) =>
                TypedResults.Ok(await controls.ReadAsync(cancellationToken)))
            .WithTags("Release discovery");

        routes.MapPost(
            "/api/releases/discovery-routines/run-now",
            async (
                ReleaseDiscoveryRunNowRequest request,
                ReleaseDiscoveryControls controls,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await controls.RunNowAsync(request, cancellationToken)))
            .WithTags("Release discovery");

        routes.MapPost(
            "/api/releases/{releaseId:long}/download/preview",
            async Task<Results<Ok<DownloadPreview>, NotFound>> (
                long releaseId,
                DownloadPreviewRequest request,
                PersonDownloads downloads,
                CancellationToken cancellationToken) =>
            {
                var preview = await downloads.PreviewAsync(
                    request.VideoId,
                    releaseId,
                    cancellationToken);
                return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
            }).WithTags("Release discovery");

        routes.MapPost(
            "/api/releases/{releaseId:long}/download",
            async Task<Results<Ok<DownloadVerdict>, BadRequest<ProblemDetails>, NotFound>> (
                long releaseId,
                DownloadRequest request,
                PersonDownloads downloads,
                CancellationToken cancellationToken) =>
            {
                if (request.DownloadId.Version != 7)
                {
                    return TypedResults.BadRequest(new ProblemDetails
                    {
                        Title = "The Download identifier is not a preview.",
                        Detail = "Use the UUIDv7 returned by the Download preview.",
                    });
                }

                var verdict = await downloads.DownloadAsync(
                    request.DownloadId,
                    request.VideoId,
                    releaseId,
                    cancellationToken);
                return verdict is null ? TypedResults.NotFound() : TypedResults.Ok(verdict);
            }).WithTags("Release discovery");
    }

    private static async Task<Results<Ok<ReleasePage>, BadRequest<ProblemDetails>, NotFound>> ReadAsync(
        ReleaseBrowse browse,
        CancellationToken cancellationToken,
        Guid? video = null,
        Guid? site = null,
        Guid? actor = null,
        IdentificationState? state = null,
        Guid? indexer = null,
        int page = 1)
    {
        if (new[] { video, site, actor }.Count(value => value is not null) != 1)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Choose one Release context.",
                Detail = "Exactly one of video, site, or actor is required.",
            });
        }

        var answer = video is not null
            ? await browse.VideoAsync(video.Value, state, indexer, page, cancellationToken)
            : site is not null
                ? await browse.SiteAsync(site.Value, state, indexer, page, cancellationToken)
                : await browse.ActorAsync(actor!.Value, state, indexer, page, cancellationToken);

        return answer is null ? TypedResults.NotFound() : TypedResults.Ok(answer);
    }
}

public sealed record DownloadPreviewRequest(Guid VideoId);

public sealed record DownloadRequest(Guid DownloadId, Guid VideoId);
