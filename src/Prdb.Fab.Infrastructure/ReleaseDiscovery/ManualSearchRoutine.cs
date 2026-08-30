using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Runs one durable per-Indexer part of a person-requested search per turn.</summary>
public sealed class ManualSearchRoutine(
    FabDbContext context,
    IndexerSearch indexers,
    ReleaseRows releases,
    ReleaseEviction eviction,
    IRoutineStore routines,
    TimeProvider time,
    ILogger<ManualSearchRoutine> logger) : IRoutine, ITargetedRoutine, IOneShot
{
    public string Name => DiscoveryRoutineNames.ManualSearch;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        await context.ManualSearches
            .Where(search => context.ManualSearchIndexers.Any(part => part.SearchId == search.Id
                && (part.State == ManualSearchIndexerState.Queued
                    || part.State == ManualSearchIndexerState.Searching
                    || part.State == ManualSearchIndexerState.Deferred)))
            .Select(search => ManualSearches.Target(search.Id))
            .ToListAsync(cancellationToken);

    public Task<bool> StartsAsync(CancellationToken cancellationToken) =>
        context.ManualSearchIndexers.AnyAsync(part =>
            part.State == ManualSearchIndexerState.Queued
            || part.State == ManualSearchIndexerState.Searching
            || part.State == ManualSearchIndexerState.Deferred,
            cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var searchId)) return RunResult.NothingToDo;

        var interrupted = await context.ManualSearchIndexers.AsTracking()
            .Where(part => part.SearchId == searchId && part.State == ManualSearchIndexerState.Searching)
            .ToListAsync(cancellationToken);
        foreach (var part in interrupted)
        {
            part.State = ManualSearchIndexerState.Failed;
            part.FinishedAt = time.GetUtcNow();
            part.Detail = "The previous attempt was interrupted before it produced a durable answer.";
        }
        if (interrupted.Count > 0) await context.SaveChangesAsync(cancellationToken);

        var now = time.GetUtcNow();
        var partToRun = await context.ManualSearchIndexers.AsTracking()
            .Where(part => part.SearchId == searchId
                           && (part.State == ManualSearchIndexerState.Queued
                               || (part.State == ManualSearchIndexerState.Deferred
                                   && part.DeferredUntil <= now)))
            .OrderBy(part => part.Indexer!.Rank)
            .ThenBy(part => part.Indexer!.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (partToRun is null)
        {
            var deferredUntil = await context.ManualSearchIndexers
                .Where(part => part.SearchId == searchId && part.State == ManualSearchIndexerState.Deferred)
                .MinAsync(part => part.DeferredUntil, cancellationToken);
            if (deferredUntil is { } due)
            {
                return RunResult.Deferred(due > now ? due - now : TimeSpan.Zero, "The Indexer query budget is exhausted.");
            }
            await routines.RetireAsync(Name, target, cancellationToken);
            return RunResult.NothingToDo;
        }

        var query = await context.ManualSearches.Where(search => search.Id == searchId)
            .Select(search => search.Query).SingleAsync(cancellationToken);
        partToRun.State = ManualSearchIndexerState.Searching;
        partToRun.StartedAt = now;
        partToRun.FinishedAt = null;
        partToRun.DeferredUntil = null;
        partToRun.Detail = null;
        await context.SaveChangesAsync(cancellationToken);

        var searched = await indexers.PageAsync(
            partToRun.IndexerId,
            page: 0,
            maxAgeDays: null,
            purpose: IndexerQueryPurpose.ManualSearch,
            query,
            cancellationToken);
        if (searched.DeferredFor is { } wait)
        {
            partToRun.State = ManualSearchIndexerState.Deferred;
            partToRun.DeferredUntil = time.GetUtcNow() + wait;
            partToRun.Detail = "Waiting for unreserved Daily Query Budget.";
            await context.SaveChangesAsync(cancellationToken);
            if (await HasQueuedAsync(searchId, cancellationToken))
            {
                return RunResult.Handled(0, "One Indexer is deferred; another selected Indexer remains queued.");
            }
            return RunResult.Deferred(wait, partToRun.Detail);
        }

        var read = searched.Read!;
        if (read.Refusal is { } refusal)
        {
            partToRun.State = ManualSearchIndexerState.Failed;
            partToRun.FinishedAt = time.GetUtcNow();
            partToRun.Detail = $"The Indexer refused the search ({refusal}).";
            await context.SaveChangesAsync(cancellationToken);
            if (await HasQueuedAsync(searchId, cancellationToken))
            {
                return RunResult.Handled(0, partToRun.Detail);
            }
            return RunResult.Failed(partToRun.Detail, read.RetryAfter);
        }

        var write = await releases.UpsertAsync(
            partToRun.IndexerId,
            read.Releases,
            time.GetUtcNow(),
            ReleaseSource.ManualSearch,
            cancellationToken);
        foreach (var releaseId in write.ReleaseIds)
        {
            if (!await context.ManualSearchResults.AnyAsync(
                    result => result.SearchId == searchId && result.ReleaseId == releaseId,
                    cancellationToken))
            {
                context.ManualSearchResults.Add(new ManualSearchResultRow
                {
                    SearchId = searchId,
                    ReleaseId = releaseId,
                });
            }
        }
        partToRun.State = ManualSearchIndexerState.Searched;
        partToRun.FinishedAt = time.GetUtcNow();
        partToRun.ResultsSeen = read.Releases.Count + read.DroppedWithoutIdentity;
        partToRun.RowsAdded = write.Added;
        partToRun.Detail = write.CacheOverBy > 0
            ? "The Indexer Cache is over its safe ceiling; these search results remain pinned while this search is retained."
            : null;
        await context.SaveChangesAsync(cancellationToken);

        var bounded = await eviction.EvictAsync(partToRun.IndexerId, cancellationToken: cancellationToken);
        if (bounded.OverBy > 0)
        {
            partToRun.Detail = "The Indexer Cache is over its safe ceiling; these search results remain pinned while this search is retained.";
            await context.SaveChangesAsync(cancellationToken);
        }

        var hasMore = await context.ManualSearchIndexers.AnyAsync(part => part.SearchId == searchId
            && (part.State == ManualSearchIndexerState.Queued
                || part.State == ManualSearchIndexerState.Searching
                || part.State == ManualSearchIndexerState.Deferred), cancellationToken);
        if (!hasMore) await routines.RetireAsync(Name, target, cancellationToken);

        logger.LogInformation(
            "Manual Search {SearchId} searched one Indexer, saw {Seen} result(s), and added {Added} Release(s).",
            searchId,
            partToRun.ResultsSeen,
            partToRun.RowsAdded);
        return RunResult.Discovered(partToRun.ResultsSeen, partToRun.RowsAdded);
    }

    private Task<bool> HasQueuedAsync(Guid searchId, CancellationToken cancellationToken) =>
        context.ManualSearchIndexers.AnyAsync(part =>
            part.SearchId == searchId && part.State == ManualSearchIndexerState.Queued,
            cancellationToken);
}
