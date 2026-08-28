using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class IndexerWalkRoutine(
    FabDbContext context,
    IndexerSearch search,
    ReleaseRows releases,
    DiscoveryState discovery,
    TimeProvider time,
    ILogger<IndexerWalkRoutine> logger) : IRoutine, ITargetedRoutine
{
    private const int PageCeiling = 10;

    public string Name => DiscoveryRoutineNames.Walk;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        await context.Indexers.Where(row => row.Enabled).Select(row => row.Id.ToString()).ToListAsync(cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId)) return RunResult.NothingToDo;
        var state = await context.IndexerWalkStates.AsTracking().SingleOrDefaultAsync(row => row.IndexerId == indexerId, cancellationToken);
        if (state is null || DiscoveryState.DeserialiseNames(state.MissingCategoryNames).Count > 0) return RunResult.NothingToDo;

        var oldDate = state.WatermarkPostDate;
        var oldIdentity = state.WatermarkReleaseId;
        var seen = 0;
        var added = 0;
        DateTimeOffset? oldest = null;
        var stopped = false;

        for (var page = 0; page < PageCeiling; page++)
        {
            var searched = await search.PageAsync(indexerId, page, maxAgeDays: null, cancellationToken);
            if (searched.DeferredFor is { } wait) return RunResult.Deferred(wait);
            var read = searched.Read!;
            if (read.Refusal is not null) return RunResult.Failed("The indexer refused a release search.", read.RetryAfter);

            var known = await context.Releases.AnyAsync(
                row => row.IndexerId == indexerId && read.Releases.Select(item => item.DerivedReleaseId).Contains(row.DerivedReleaseId),
                cancellationToken);
            var write = await releases.UpsertAsync(indexerId, read.Releases, time.GetUtcNow(), ReleaseSource.IndexerWalk, cancellationToken);
            seen += read.Releases.Count + read.DroppedWithoutIdentity;
            added += write.Added;
            oldest = read.Releases.Count == 0 ? oldest : read.Releases.Min(item => item.PostDate);

            if (page == 0 && read.Releases.Count > 0)
            {
                var newest = read.Releases.OrderByDescending(item => item.PostDate).ThenBy(item => item.DerivedReleaseId, StringComparer.Ordinal).First();
                if (oldDate is null || newest.PostDate > oldDate)
                {
                    state.WatermarkPostDate = newest.PostDate;
                    state.WatermarkReleaseId = newest.DerivedReleaseId;
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            var returned = read.Releases.Count + read.DroppedWithoutIdentity;
            stopped = returned < Connections.NewznabGateway.PageSize
                || known
                || (oldDate is not null && read.Releases.Any(item => item.PostDate < oldDate))
                || (oldIdentity is not null && read.Releases.Any(item => item.DerivedReleaseId == oldIdentity));
            if (stopped) break;
        }

        if (!stopped && state.BootstrapCompletedAt is not null && oldDate is not null && oldest is not null)
        {
            await discovery.OpenCatchUpAsync(indexerId, oldDate.Value, time.GetUtcNow(), "missed paging window", cancellationToken);
        }

        logger.LogInformation("The indexer walk saw {Seen} result(s) and added {Added} release(s).", seen, added);
        return RunResult.Discovered(seen, added);
    }
}
