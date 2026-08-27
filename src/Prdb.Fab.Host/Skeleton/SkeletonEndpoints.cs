using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Skeleton;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Skeleton;

namespace Prdb.Fab.Host.Skeleton;

/// <summary>
/// The one route, end to end. Scaffolding — but the shape is not: this is where
/// ADR 0040's contract is exercised before anything depends on it.
/// </summary>
public static class SkeletonEndpoints
{
    /// <summary>ADR 0040: offsets, and the page lives in the address.</summary>
    public const int PageSize = 20;

    public static void MapSkeleton(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/skeleton").WithTags("Skeleton");

        group.MapGet("/items", async (
            SkeletonItems items,
            CancellationToken cancellationToken,
            int page = 1) =>
        {
            var all = await items.ListAsync(cancellationToken);
            var wanted = Math.Max(page, 1);

            return TypedResults.Ok(new ItemPage(
                Items: [.. all.Skip((wanted - 1) * PageSize).Take(PageSize)],
                Page: wanted,
                PageSize: PageSize,
                Total: all.Count));
        });

        group.MapPost("/items", async (
            AddItemRequest request,
            SkeletonItems items,
            CancellationToken cancellationToken) =>
        {
            var label = request.Label?.Trim();

            // ADR 0040: a verdict is HTTP 200 with a typed body. This one is a
            // refusal rather than a failure — the caller asked something the
            // tool will not do, which is not the same as the tool being broken,
            // and TanStack Query would retry a 5xx.
            if (string.IsNullOrEmpty(label))
            {
                return TypedResults.Ok(new AddItemVerdict(Added: null, Refusal: "An item needs a label."));
            }

            var added = await items.AddAsync(label, cancellationToken);

            return TypedResults.Ok(new AddItemVerdict(added, Refusal: null));
        });

        // ADR 0040: a named action, not a write to a state field. ADR 0029's
        // log has to be able to say which act happened, and "the due time
        // changed" does not say it.
        group.MapPost("/sweep/run-now", async (
            IRoutineStore store,
            CancellationToken cancellationToken) =>
        {
            var found = await store.RunNowAsync(SkeletonSweep.RoutineName, target: null, cancellationToken);

            // ADR 0038: this is the whole of running something now. The lane
            // picks the row up on its next tick, which is at most a second
            // away, and the run is governed like any other.
            return TypedResults.Ok(new RunNowVerdict(
                Accepted: found,
                Detail: found
                    ? "The sweep is due now and the bulk lane will take it on its next tick."
                    : "There is no row for the sweep, so there is nothing to make due."));
        });

        group.MapGet("/runs", async (RunLog log, CancellationToken cancellationToken) =>
            TypedResults.Ok(await log.RecentAsync(SkeletonSweep.RoutineName, count: 20, cancellationToken)));
    }
}

public sealed record ItemPage(IReadOnlyList<SkeletonItem> Items, int Page, int PageSize, int Total);

public sealed record AddItemRequest(string? Label);

/// <summary>ADR 0040: a verdict is a success with a typed body saying what happened.</summary>
public sealed record AddItemVerdict(SkeletonItem? Added, string? Refusal);

public sealed record RunNowVerdict(bool Accepted, string Detail);
