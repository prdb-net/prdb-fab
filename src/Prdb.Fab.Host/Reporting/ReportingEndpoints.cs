using Prdb.Fab.Infrastructure.Reporting;

namespace Prdb.Fab.Host.Reporting;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReporting(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.MapGroup("/api/settings/reporting").WithTags("Reporting");

        settings.MapGet("/", async (
            ReportingSettings reporting,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await reporting.ReadAsync(cancellationToken)));

        settings.MapPost("/", async (
            ReportingSettingsRequest request,
            ReportingSettings reporting,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await reporting.SaveAsync(
                request.ReportFulfilments,
                request.ReportConfirmedAssignments,
                cancellationToken)));

        return endpoints;
    }
}

public sealed record ReportingSettingsRequest(
    bool ReportFulfilments,
    bool ReportConfirmedAssignments);
