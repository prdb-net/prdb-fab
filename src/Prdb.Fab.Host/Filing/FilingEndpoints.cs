using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;

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
    }
}

public sealed record IdentificationSettingsState(AfterDownloadGateChoice AfterDownload);

public sealed record IdentificationSettingsRequest(AfterDownloadGateChoice AfterDownload);

public sealed record IdentificationSettingsVerdict(
    AfterDownloadGateChoice AfterDownload,
    int Reconsidered);
