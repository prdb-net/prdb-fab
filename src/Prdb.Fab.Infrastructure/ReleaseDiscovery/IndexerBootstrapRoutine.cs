using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class IndexerBootstrapRoutine(
    FabDbContext context,
    IndexerSearch search,
    ReleaseRows releases,
    IRoutineStore routines,
    TimeProvider time,
    ILogger<IndexerBootstrapRoutine> logger) : IRoutine, IOneShot, ITargetedRoutine
{
    public string Name => DiscoveryRoutineNames.Bootstrap;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public Task<bool> StartsAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public async Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        await context.IndexerWalkStates
            .Where(state => state.BootstrapCompletedAt == null)
            .Select(state => state.IndexerId.ToString())
            .ToListAsync(cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId)) return RunResult.NothingToDo;
        var state = await context.IndexerWalkStates.AsTracking().SingleOrDefaultAsync(row => row.IndexerId == indexerId, cancellationToken);
        if (state is null || state.BootstrapCompletedAt is not null) return RunResult.NothingToDo;
        if (DiscoveryState.DeserialiseNames(state.MissingCategoryNames).Count > 0) return RunResult.NothingToDo;

        var page = state.ResumePage ?? 0;
        var searched = await search.PageAsync(indexerId, page, maxAgeDays: 90, cancellationToken);
        if (searched.DeferredFor is { } wait) return RunResult.Deferred(wait);
        var read = searched.Read!;
        if (read.Refusal is not null) return RunResult.Failed("The indexer refused the bootstrap search.", read.RetryAfter);

        var write = await releases.UpsertAsync(indexerId, read.Releases, time.GetUtcNow(), ReleaseSource.IndexerWalk, cancellationToken);

        // The release batch is committed by UpsertAsync before the durable
        // page moves. A crash between them repeats an idempotent page; it never skips one.
        state.ResumePage = page + 1;
        if (read.Releases.Count + read.DroppedWithoutIdentity < Connections.NewznabGateway.PageSize)
        {
            state.BootstrapCompletedAt = time.GetUtcNow();
            state.ResumePage = null;
        }
        await context.SaveChangesAsync(cancellationToken);

        if (state.BootstrapCompletedAt is not null)
        {
            await routines.RetireAsync(Name, target, cancellationToken);
            logger.LogInformation("The 90-day indexer bootstrap has retired after page {Page}.", page);
        }

        return RunResult.Discovered(read.Releases.Count + read.DroppedWithoutIdentity, write.Added);
    }
}
