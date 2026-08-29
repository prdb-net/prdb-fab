using Prdb.Fab.Infrastructure.Status;

namespace Prdb.Fab.Host.Status;

public static class StatusEndpoints
{
    public static void MapStatus(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/status", async (StatusService status, CancellationToken cancellationToken) =>
            TypedResults.Ok(await status.ReadAsync(cancellationToken)))
            .WithTags("Status");

        routes.MapPost("/api/status/run-now", async (
            StatusRunNowRequest request,
            StatusService status,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await status.RunNowAsync(request, cancellationToken)))
            .WithTags("Status");
    }
}
