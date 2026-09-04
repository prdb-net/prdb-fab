using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Fills Actor profile artwork in the background, never from a browse request.</summary>
public sealed class ActorProfileRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    ActorDetails details,
    TimeProvider time) : IRoutine
{
    public const string RoutineName = "prdb.actor-profiles";

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var next = await context.CatalogueActors
            .AsNoTracking()
            .Where(row => row.ProfileCheckedAt == null)
            .OrderByDescending(row => context.FavouriteActors.Any(favourite => favourite.ActorId == row.Id))
            .ThenByDescending(row => context.CatalogueVideoActors.Count(credit => credit.ActorId == row.Id))
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.PrdbId })
            .Take(50)
            .ToListAsync(cancellationToken);
        if (next.Count == 0) return RunResult.NothingToDo;

        var apiKey = await context.Installation.Select(row => row.PrdbApiKey).SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey)) return RunResult.NothingToDo;

        var actors = await prdb.AskAsync(
            apiKey,
            PrdbWork.Actors,
            (client, token) => client.Actors.Batch.PostAsync(
                new GetActorsByIdsRequest { Ids = [.. next.Select(row => row.PrdbId)] },
                cancellationToken: token),
            cancellationToken) ?? [];
        var returnedIds = actors.Where(actor => actor.Id.HasValue).Select(actor => actor.Id!.Value).ToHashSet();
        var written = await details.WriteDetailsAsync(actors, cancellationToken);

        // A batch silently omits an id that no longer exists. Mark that lookup
        // complete so this fallback does not ask for the same tombstone every
        // ten seconds while the Actor feed catches up and removes it.
        var checkedAt = time.GetUtcNow();
        await context.CatalogueActors
            .Where(row => next.Select(item => item.Id).Contains(row.Id) && !returnedIds.Contains(row.PrdbId))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProfileCheckedAt, checkedAt), cancellationToken);
        return RunResult.Handled(written);
    }
}
