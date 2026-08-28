using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Infrastructure.Acquisition;

namespace Prdb.Fab.Host.Acquisition;

/// <summary>Local Download history and actions that never mutate SABnzbd.</summary>
public static class AcquisitionEndpoints
{
    public static void MapAcquisition(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/api/downloads",
            async Task<Ok<DownloadPage>> (
                DownloadBrowse downloads,
                CancellationToken cancellationToken,
                DownloadState? state = null,
                Guid? indexer = null,
                int page = 1) => TypedResults.Ok(await downloads.ReadAsync(
                    state, indexer, page, cancellationToken)))
            .WithTags("Acquisition");

        routes.MapPost(
            "/api/downloads/stop-following/preview",
            async Task<Ok<DownloadSelectionPreview>> (
                DownloadSelectionRequest request,
                DownloadBrowse downloads,
                CancellationToken cancellationToken) => TypedResults.Ok(
                    await downloads.PreviewStopFollowingAsync(request.DownloadIds, cancellationToken)))
            .WithTags("Acquisition");

        routes.MapPost(
            "/api/downloads/stop-following",
            async Task<Ok<DownloadSelectionVerdict>> (
                DownloadSelectionRequest request,
                DownloadBrowse downloads,
                CancellationToken cancellationToken) => TypedResults.Ok(
                    await downloads.StopFollowingAsync(request.DownloadIds, cancellationToken)))
            .WithTags("Acquisition");

        routes.MapPost(
            "/api/releases/video/{videoId:guid}/reset-downloads/preview",
            async Task<Ok<DownloadResetPreview>> (
                Guid videoId,
                DownloadBrowse downloads,
                CancellationToken cancellationToken) => TypedResults.Ok(
                    await downloads.PreviewResetAsync(videoId, cancellationToken)))
            .WithTags("Acquisition");

        routes.MapPost(
            "/api/releases/video/{videoId:guid}/reset-downloads",
            async Task<Ok<DownloadResetVerdict>> (
                Guid videoId,
                DownloadResetRequest request,
                DownloadBrowse downloads,
                CancellationToken cancellationToken) => TypedResults.Ok(
                    await downloads.ResetAsync(videoId, request.DownloadIds, cancellationToken)))
            .WithTags("Acquisition");
    }
}

public sealed record DownloadSelectionRequest(IReadOnlyList<Guid> DownloadIds);
public sealed record DownloadResetRequest(IReadOnlyList<Guid> DownloadIds);
