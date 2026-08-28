using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Searches one Indexer directly for the least-recently-searched Wanted Videos.</summary>
public sealed class WantedSweepRoutine(
    FabDbContext context,
    IndexerSearch search,
    ReleaseRows releases,
    TimeProvider time,
    ILogger<WantedSweepRoutine> logger) : IRoutine, ITargetedRoutine
{
    public const int VideosPerRun = 5;

    public string Name => DiscoveryRoutineNames.WantedSweep;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromMinutes(15);

    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        IndexerTargets.CanonicalAsync(
            context.Indexers.Where(row => row.Enabled).Select(row => row.Id),
            cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId))
        {
            return RunResult.NothingToDo;
        }

        var enabled = await context.Indexers.AnyAsync(
            row => row.Id == indexerId && row.Enabled,
            cancellationToken);
        if (!enabled)
        {
            return RunResult.NothingToDo;
        }

        var ordered = await (
                from wanted in context.WantedVideos
                join state in context.WantedVideoSweepStates.Where(row => row.IndexerId == indexerId)
                    on wanted.VideoId equals state.VideoId into states
                from state in states.DefaultIfEmpty()
                orderby state.LastSearchedAt, wanted.VideoId
                select new DueWanted(wanted.VideoId, wanted.Video!.Title, state))
            .ToListAsync(cancellationToken);

        var due = ordered
            .Select(wanted => wanted with { Query = WantedSearchTitle.Of(wanted.Title) })
            .Where(wanted => WantedSearchTitle.IsSearchable(wanted.Query))
            .Take(VideosPerRun)
            .ToArray();

        if (due.Length == 0)
        {
            return RunResult.NothingToDo;
        }

        var searchedCount = 0;
        var resultsSeen = 0;
        var rowsAdded = 0;

        foreach (var wanted in due)
        {
            var searched = await search.PageAsync(
                indexerId,
                page: 0,
                maxAgeDays: null,
                purpose: IndexerQueryPurpose.WantedSweep,
                query: wanted.Query,
                cancellationToken: cancellationToken);

            if (searched.DeferredFor is { } wait)
            {
                return searchedCount == 0
                    ? RunResult.Deferred(wait)
                    : RunResult.Discovered(resultsSeen, rowsAdded);
            }

            var read = searched.Read!;
            if (read.Refusal is not null)
            {
                return RunResult.Failed("The indexer refused a Wanted Sweep search.", read.RetryAfter);
            }

            var write = await releases.UpsertAsync(
                indexerId,
                read.Releases,
                time.GetUtcNow(),
                ReleaseSource.WantedSweep,
                cancellationToken);
            if (write.CacheOverBy > 0)
            {
                return RunResult.Failed("The Indexer Cache cannot hold its ceiling without losing an unexamined or pinned Release.");
            }
            resultsSeen += read.Releases.Count + read.DroppedWithoutIdentity;
            rowsAdded += write.Added;
            searchedCount++;

            // ADR 0024: an empty answer changes no ordering state. The oldest,
            // hardest-to-find video stays at the front rather than being
            // penalised for exactly the absence this sweep exists to revisit.
            if (read.Releases.Count + read.DroppedWithoutIdentity == 0)
            {
                continue;
            }

            var state = wanted.State;
            if (state is null)
            {
                state = new WantedVideoSweepStateRow
                {
                    VideoId = wanted.VideoId,
                    IndexerId = indexerId,
                };
                context.WantedVideoSweepStates.Add(state);
            }

            state.LastSearchedAt = time.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "The Wanted Sweep searched {Count} video title(s), saw {Seen} result(s) and added {Added} Release(s).",
            searchedCount,
            resultsSeen,
            rowsAdded);

        return RunResult.Discovered(resultsSeen, rowsAdded);
    }

    private sealed record DueWanted(
        long VideoId,
        string Title,
        WantedVideoSweepStateRow? State,
        string Query = "");
}
