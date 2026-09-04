using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Starts and describes a durable fill of one Actor's latest Videos.</summary>
public sealed class ActorVideoLoads(FabDbContext context, TimeProvider time)
{
    public const int Limit = 500;

    public async Task<ActorVideoLoadStart> StartAsync(Guid actorPrdbId, CancellationToken cancellationToken)
    {
        var actorId = await context.CatalogueActors
            .Where(row => row.PrdbId == actorPrdbId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (actorId is null)
        {
            return new(ActorVideoLoadStartOutcome.ActorNotFound, null);
        }

        var state = await context.ActorVideoLoadStates.AsTracking()
            .SingleOrDefaultAsync(row => row.ActorId == actorId, cancellationToken);
        if (state is { CompletedAt: null })
        {
            return new(ActorVideoLoadStartOutcome.AlreadyRunning, View(state));
        }

        var now = time.GetUtcNow();
        if (state is null)
        {
            state = new ActorVideoLoadStateRow { ActorId = actorId.Value };
            context.ActorVideoLoadStates.Add(state);
        }
        state.ResumePage = 1;
        state.VideosSeen = 0;
        state.RequestedAt = now;
        state.CompletedAt = null;

        // A refresh replaces the previous bounded result. Anything else that
        // still points at one of those videos continues to pin it independently.
        await context.ActorVideoLoadVideos
            .Where(row => row.ActorId == actorId)
            .ExecuteDeleteAsync(cancellationToken);

        var target = Target(actorPrdbId);
        var routine = await context.Routines.AsTracking()
            .SingleOrDefaultAsync(row => row.Name == ActorVideoLoadRoutine.RoutineName && row.Target == target,
                cancellationToken);
        if (routine is null)
        {
            context.Routines.Add(new RoutineRow
            {
                Name = ActorVideoLoadRoutine.RoutineName,
                Target = target,
                Lane = Lane.Bulk,
                DueAt = now,
            });
        }
        else
        {
            routine.DueAt = now;
        }
        await context.SaveChangesAsync(cancellationToken);
        return new(ActorVideoLoadStartOutcome.Started, View(state));
    }

    internal static string Target(Guid actorPrdbId) => actorPrdbId.ToString("D");

    internal static ActorVideoLoadView View(ActorVideoLoadStateRow state) => new(
        Active: state.CompletedAt is null,
        state.VideosSeen,
        Limit,
        state.RequestedAt,
        state.CompletedAt);
}

public enum ActorVideoLoadStartOutcome
{
    Started,
    AlreadyRunning,
    ActorNotFound,
}

public sealed record ActorVideoLoadStart(ActorVideoLoadStartOutcome Outcome, ActorVideoLoadView? Load);

public sealed record ActorVideoLoadView(
    bool Active,
    int VideosSeen,
    int Limit,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);
