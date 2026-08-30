using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>
/// Repeats a complete ninety-day Indexer pass, independently of the head
/// watermark, so late visibility and downtime cannot leave a permanent hole.
/// </summary>
public sealed class IndexerRecentWindowRoutine(
    FabDbContext context,
    IndexerSearch search,
    ReleaseRows releases,
    TimeProvider time,
    ILogger<IndexerRecentWindowRoutine> logger) : IRoutine, ITargetedRoutine
{
    public string Name => DiscoveryRoutineNames.RecentWindow;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        IndexerTargets.CanonicalAsync(
            context.Indexers.Where(row => row.Enabled).Select(row => row.Id),
            cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId)) return RunResult.NothingToDo;

        var state = await context.IndexerWalkStates
            .AsTracking()
            .SingleOrDefaultAsync(row => row.IndexerId == indexerId, cancellationToken);
        if (state is null || DiscoveryState.DeserialiseNames(state.MissingCategoryNames).Count > 0)
        {
            return RunResult.NothingToDo;
        }

        var now = time.GetUtcNow();
        var started = state.RecentWindowPassStartedAt ?? now;
        var page = Math.Max(0, state.RecentWindowResumePage);

        if (state.RecentWindowPassStartedAt is null)
        {
            state.RecentWindowPassStartedAt = started;
            state.RecentWindowOldestPostDate = null;
        }

        var searched = await search.PageAsync(
            indexerId,
            page,
            RecentWindow.Days,
            IndexerQueryPurpose.Walk,
            query: null,
            cancellationToken);
        if (searched.DeferredFor is { } wait) return RunResult.Deferred(wait);

        var read = searched.Read!;
        if (read.Refusal is not null)
        {
            return RunResult.Failed("The Indexer refused its Recent Window pass.", read.RetryAfter);
        }

        var write = await releases.UpsertAsync(
            indexerId,
            read.Releases,
            now,
            ReleaseSource.IndexerWalk,
            cancellationToken);

        DateTimeOffset? oldest = read.Releases.Count == 0
            ? null
            : read.Releases.Min(item => item.PostDate);
        state.RecentWindowOldestPostDate = Oldest(state.RecentWindowOldestPostDate, oldest);
        var returned = read.Releases.Count + read.DroppedWithoutIdentity;
        var reachedBoundary = returned < Connections.NewznabGateway.PageSize
            || oldest is { } observed && observed <= RecentWindow.BeginsAt(started);

        TimeSpan dueIn;
        if (reachedBoundary)
        {
            state.RecentWindowCompletedAt = now;
            state.RecentWindowResumePage = 0;
            state.RecentWindowPassStartedAt = null;
            dueIn = RecentWindow.NextPassIn(started, now);
            logger.LogInformation(
                "The Indexer Recent Window completed page {Page}; {Added} new Release(s) were written.",
                page,
                write.Added);
        }
        else
        {
            state.RecentWindowResumePage = page + 1;
            dueIn = TimeSpan.Zero;
        }

        await context.SaveChangesAsync(cancellationToken);
        return RunResult.Discovered(returned, write.Added, dueIn);
    }

    private static DateTimeOffset? Oldest(DateTimeOffset? held, DateTimeOffset? read) =>
        held is null ? read : read is null || held <= read ? held : read;
}
