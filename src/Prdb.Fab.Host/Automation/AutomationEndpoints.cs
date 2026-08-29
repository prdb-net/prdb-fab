using Microsoft.AspNetCore.Http.HttpResults;

using Prdb.Fab.Infrastructure.Automation;

namespace Prdb.Fab.Host.Automation;

public static class AutomationEndpoints
{
    public static void MapAutomation(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/settings/automation").WithTags("Automation");

        group.MapGet("/", async (
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.ReadAsync(cancellationToken)));

        group.MapPost("/cap", async (
            AutomationCapRequest request,
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.SaveCapAsync(request.AutomaticDownloadCap, cancellationToken)));

        group.MapGet("/rules/{id:guid}", async Task<Results<Ok<AutomationRuleView>, NotFound>> (
            Guid id,
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
        {
            var rule = await settings.ReadRuleAsync(id, cancellationToken);
            return rule is null ? TypedResults.NotFound() : TypedResults.Ok(rule);
        });

        group.MapPost("/rules", async (
            AutomationRuleRequest request,
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.SaveRuleAsync(
                null,
                request.Name,
                request.Enabled,
                request.MinimumSize,
                request.MaximumSize,
                request.AllowedIndexerIds,
                cancellationToken)));

        group.MapPost("/rules/{id:guid}", async Task<Results<Ok<AutomationRuleVerdict>, NotFound>> (
            Guid id,
            AutomationRuleRequest request,
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return TypedResults.Ok(await settings.SaveRuleAsync(
                    id,
                    request.Name,
                    request.Enabled,
                    request.MinimumSize,
                    request.MaximumSize,
                    request.AllowedIndexerIds,
                    cancellationToken));
            }
            catch (AutomationRuleNotFoundException)
            {
                return TypedResults.NotFound();
            }
        });

        group.MapPost("/rules/{id:guid}/delete/preview", async Task<Results<Ok<AutomationRuleDeletePreview>, NotFound>> (
            Guid id,
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
        {
            var preview = await settings.PreviewDeleteAsync(id, cancellationToken);
            return preview is null ? TypedResults.NotFound() : TypedResults.Ok(preview);
        });

        group.MapPost("/rules/{id:guid}/delete", async Task<Results<Ok<AutomationRuleDeleteVerdict>, NotFound>> (
            Guid id,
            AutomationRuleSettings settings,
            CancellationToken cancellationToken) =>
        {
            var verdict = await settings.DeleteAsync(id, cancellationToken);
            return verdict is null ? TypedResults.NotFound() : TypedResults.Ok(verdict);
        });
    }
}

public sealed record AutomationCapRequest(int AutomaticDownloadCap);
public sealed record AutomationRuleRequest(
    string? Name,
    bool Enabled,
    long? MinimumSize,
    long? MaximumSize,
    IReadOnlyList<Guid> AllowedIndexerIds);
