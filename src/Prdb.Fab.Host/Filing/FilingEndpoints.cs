using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Host.Filing;

public static class FilingEndpoints
{
    public static void MapFiling(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/settings/identification").WithTags("Filing");

        group.MapGet("/", async (
            IdentificationSettings settings,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new IdentificationSettingsState(
                await settings.ReadAsync(cancellationToken))));

        group.MapPost("/", async (
            IdentificationSettingsRequest request,
            IdentificationSettings settings,
            CancellationToken cancellationToken) =>
        {
            var reconsidered = await settings.SaveAsync(request.AfterDownload, cancellationToken);
            return TypedResults.Ok(new IdentificationSettingsVerdict(request.AfterDownload, reconsidered));
        });

        var library = routes.MapGroup("/api/settings/library").WithTags("Filing");
        library.MapGet("/", async (
            LibrarySettings settings,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.ReadAsync(cancellationToken)));
        library.MapPost("/", async (
            LibrarySettingsRequest request,
            LibrarySettings settings,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.SaveAsync(request.DeleteLeftovers, cancellationToken)));

        var review = routes.MapGroup("/api/review-queue").WithTags("Filing");
        review.MapGet("/", async (
            ReviewQueue queue,
            CancellationToken cancellationToken,
            ArrivingFileReason? reason = null,
            Guid? download = null,
            int page = 1) =>
            TypedResults.Ok(await queue.ReadAsync(reason, download, page, cancellationToken)));
        review.MapGet("/count", async (
            FabDbContext context,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(new ReviewQueueCount(
                await context.ArrivingFiles.CountAsync(row => row.Reason != null, cancellationToken))));
        review.MapGet("/videos", async (
            ReviewVideoSearch searcher,
            CancellationToken cancellationToken,
            string? search = null,
            Guid? site = null,
            int page = 1) =>
            TypedResults.Ok(await searcher.SearchAsync(search, site, page, cancellationToken)));
        review.MapPost("/delete/preview", async (
            ReviewSelectionRequest request,
            ReviewQueue queue,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await queue.PreviewAsync(request.ArrivingFileIds, cancellationToken)));
        review.MapPost("/delete", async (
            ReviewSelectionRequest request,
            ReviewQueue queue,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await queue.DeleteAsync(request.ArrivingFileIds, cancellationToken)));
        review.MapPost("/dismiss", async (
            ReviewSelectionRequest request,
            ReviewQueue queue,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await queue.DismissAsync(request.ArrivingFileIds, cancellationToken)));
        review.MapPost("/{arrivingFileId:guid}/file-as", async (
            Guid arrivingFileId,
            FileAsRequest request,
            ReviewDecisions decisions,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await decisions.FileAsAsync(arrivingFileId, request.VideoId, cancellationToken)));
        review.MapPost("/{arrivingFileId:guid}/replace", async (
            Guid arrivingFileId,
            ReviewDecisions decisions,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await decisions.ReplaceAsync(arrivingFileId, cancellationToken)));
        review.MapPost("/{arrivingFileId:guid}/file-as-only-copy", async (
            Guid arrivingFileId,
            ReviewDecisions decisions,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await decisions.FileAsOnlyCopyAsync(arrivingFileId, cancellationToken)));

        var libraryEntries = routes.MapGroup("/api/library").WithTags("Filing");
        libraryEntries.MapGet("/", async (
            LibraryBrowse browse,
            CancellationToken cancellationToken,
            string? search = null,
            Guid? site = null,
            Guid? actor = null,
            string? quality = null,
            int page = 1) =>
            TypedResults.Ok(await browse.ReadAsync(
                search, site, actor, quality, page, cancellationToken)));
        libraryEntries.MapGet("/{videoId:guid}", ReadLibraryEntryAsync);

        routes.MapGet("/api/operation-log", async (
            OperationLogBrowse log,
            CancellationToken cancellationToken,
            string? act = null,
            string? search = null,
            int page = 1) =>
            TypedResults.Ok(await log.ReadAsync(act, search, null, page, cancellationToken)))
            .WithTags("Filing");
    }

    private static async Task<Results<Ok<LibraryEntry>, NotFound>> ReadLibraryEntryAsync(
        Guid videoId,
        LibraryBrowse browse,
        CancellationToken cancellationToken)
    {
        var entry = await browse.EntryAsync(videoId, cancellationToken);
        return entry is null ? TypedResults.NotFound() : TypedResults.Ok(entry);
    }
}

public sealed record IdentificationSettingsState(AfterDownloadGateChoice AfterDownload);

public sealed record IdentificationSettingsRequest(AfterDownloadGateChoice AfterDownload);

public sealed record IdentificationSettingsVerdict(
    AfterDownloadGateChoice AfterDownload,
    int Reconsidered);

public sealed record LibrarySettingsRequest(bool DeleteLeftovers);
public sealed record ReviewSelectionRequest(IReadOnlyList<Guid> ArrivingFileIds);
public sealed record FileAsRequest(Guid VideoId);
public sealed record ReviewQueueCount(int Open);
