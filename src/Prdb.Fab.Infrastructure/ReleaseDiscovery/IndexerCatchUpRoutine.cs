using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class IndexerCatchUpRoutine(
    FabDbContext context,
    IndexerSearch search,
    ReleaseRows releases,
    IRoutineStore routines,
    TimeProvider time,
    ILogger<IndexerCatchUpRoutine> logger) : IRoutine, IOneShot, ITargetedRoutine
{
    public string Name => DiscoveryRoutineNames.CatchUp;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public Task<bool> StartsAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        IndexerTargets.CanonicalAsync(
            context.IndexerWalkStates
                .Where(state => state.CatchUpFrom != null)
                .Select(state => state.IndexerId),
            cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId)) return RunResult.NothingToDo;
        var state = await context.IndexerWalkStates.AsTracking().SingleOrDefaultAsync(row => row.IndexerId == indexerId, cancellationToken);
        if (state?.CatchUpFrom is null) return RunResult.NothingToDo;
        if (DiscoveryState.DeserialiseNames(state.MissingCategoryNames).Count > 0) return RunResult.NothingToDo;

        var from = state.CatchUpFrom.Value;
        var page = state.ResumePage ?? 0;
        var maxAge = Math.Max(1, (int)Math.Ceiling((time.GetUtcNow() - from).TotalDays));
        var searched = await search.PageAsync(
                indexerId,
                page,
                maxAge,
                purpose: IndexerQueryPurpose.Walk,
                query: null,
                cancellationToken: cancellationToken);
        if (searched.DeferredFor is { } wait) return RunResult.Deferred(wait);
        var read = searched.Read!;
        if (read.Refusal is not null) return RunResult.Failed("The indexer refused the catch-up search.", read.RetryAfter);

        var write = await releases.UpsertAsync(indexerId, read.Releases, time.GetUtcNow(), ReleaseSource.IndexerWalk, cancellationToken);
        if (write.CacheOverBy > 0)
        {
            return RunResult.Failed("The Indexer Cache cannot hold its ceiling without losing an unexamined or pinned Release.");
        }
        state.ResumePage = page + 1;
        var finished = read.Releases.Count + read.DroppedWithoutIdentity < Connections.NewznabGateway.PageSize
            || read.Releases.Any(item => item.PostDate < from);

        if (finished)
        {
            var cause = state.CatchUpCause;
            state.CatchUpFrom = null;
            state.CatchUpTo = null;
            state.ResumePage = null;
            state.CatchUpCause = null;
            await context.SaveChangesAsync(cancellationToken);
            await routines.RetireAsync(Name, target, cancellationToken);
            logger.LogInformation("The indexer catch-up for {Cause} has retired after page {Page}.", cause, page);
        }
        else
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return RunResult.Discovered(read.Releases.Count + read.DroppedWithoutIdentity, write.Added);
    }
}
