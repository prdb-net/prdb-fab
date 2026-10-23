using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Automation;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Searches one Indexer directly for the least-recently-searched Wanted Videos.</summary>
public sealed class WantedSweepRoutine(
    FabDbContext context,
    IndexerSearch search,
    WantedAutopilot autopilot,
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

        var account = await context.Installation.Select(row => row.PrdbUserHash).SingleAsync(cancellationToken);
        var policyEnabled = await context.AutomationRules.AnyAsync(row => row.SuppliedWanted && row.Enabled, cancellationToken);
        var priority = policyEnabled ? autopilot.AdmittedVideos(account).Select(row => row.Id) : context.CatalogueVideos.Where(row => false).Select(row => row.Id);
        var retryBefore = time.GetUtcNow() - Cadence;
        var ordered = await (
                from wanted in context.WantedVideos
                join state in context.WantedVideoSweepStates.Where(row => row.IndexerId == indexerId)
                    on wanted.VideoId equals state.VideoId into states
                from state in states.DefaultIfEmpty()
                where state == null || state.LastSearchedAt <= retryBefore
                orderby state.LastSearchedAt, wanted.Video!.ReleaseDate descending, wanted.Video!.PrdbId
                select new DueWanted(wanted.VideoId, wanted.Video!.Title, state,
                    priority.Contains(wanted.VideoId)
                    && !context.LibraryEntries.Any(entry => entry.VideoId == wanted.Video!.PrdbId)
                    && !context.Downloads.Any(download => download.VideoId == wanted.Video!.PrdbId
                        && (download.State == Core.Acquisition.DownloadState.Outstanding || download.State == Core.Acquisition.DownloadState.Completed))))
            .ToListAsync(cancellationToken);
        var searchable = ordered.Select(wanted => wanted with { Query = WantedSearchTitle.Of(wanted.Title) })
            .Where(wanted => WantedSearchTitle.IsSearchable(wanted.Query)).ToArray();
        // Reserve one of the five queries for other Wanted intent. Null/oldest
        // attempt first makes empty answers rotate through both groups fairly.
        var due = searchable.Where(row => row.Priority).Take(VideosPerRun - 1)
            .Concat(searchable.Where(row => !row.Priority).Take(1))
            .Concat(searchable).DistinctBy(row => row.VideoId).Take(VideosPerRun).ToArray();

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
                await RecordAttemptAsync(wanted, indexerId, cancellationToken);
                return RunResult.Failed("The indexer refused a Wanted Sweep search.", read.RetryAfter);
            }

            var write = await releases.UpsertAsync(
                indexerId,
                read.Releases,
                time.GetUtcNow(),
                ReleaseSource.WantedSweep,
                cancellationToken);
            resultsSeen += read.Releases.Count + read.DroppedWithoutIdentity;
            rowsAdded += write.Added;
            searchedCount++;

            await RecordAttemptAsync(wanted, indexerId, cancellationToken);
        }

        logger.LogInformation(
            "The Wanted Sweep searched {Count} video title(s), saw {Seen} result(s) and added {Added} Release(s).",
            searchedCount,
            resultsSeen,
            rowsAdded);

        return RunResult.Discovered(resultsSeen, rowsAdded);
    }

    private async Task RecordAttemptAsync(DueWanted wanted, Guid indexerId, CancellationToken token)
    {
        var state = wanted.State;
        if (state is null)
        {
            state = new() { VideoId = wanted.VideoId, IndexerId = indexerId };
            context.WantedVideoSweepStates.Add(state);
        }
        else context.WantedVideoSweepStates.Update(state);
        state.LastSearchedAt = time.GetUtcNow();
        await context.SaveChangesAsync(token);
    }

    private sealed record DueWanted(
        long VideoId,
        string Title,
        WantedVideoSweepStateRow? State,
        bool Priority,
        string Query = "");
}
