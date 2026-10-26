using Prdb.Fab.Infrastructure.Status;

namespace Prdb.Fab.Host.Status;

public static class StatusEndpoints
{
    public static void MapStatus(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/settings/notifications", async (NotificationSettings settings, CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.ReadAsync(cancellationToken))).WithTags("Status");
        routes.MapPost("/api/settings/notifications", async (NotificationSettingsRequest request, NotificationSettings settings, CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.SaveAsync(request.Enabled, request.WebhookUrl, cancellationToken))).WithTags("Status");
        routes.MapGet("/api/status", async (StatusSnapshot status, CancellationToken cancellationToken) =>
            TypedResults.Ok(await status.ReadAsync(cancellationToken)))
            .WithTags("Status");

        routes.MapPost("/api/status/run-now", async (
            StatusRunNowRequest request,
            StatusService status,
            StatusSnapshot snapshot,
            CancellationToken cancellationToken) =>
        {
            var result = await status.RunNowAsync(request, cancellationToken);
            await snapshot.InvalidateAsync(cancellationToken);
            return TypedResults.Ok(result);
        })
            .WithTags("Status");
    }
}

public sealed record NotificationSettingsRequest(bool Enabled, string? WebhookUrl);
