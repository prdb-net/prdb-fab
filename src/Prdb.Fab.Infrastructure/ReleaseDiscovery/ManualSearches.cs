using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Records and explains person-requested, scheduler-owned Indexer searches.</summary>
public sealed class ManualSearches(FabDbContext context, TimeProvider time)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public async Task<ManualSearchStartVerdict> StartAsync(
        Guid videoId,
        Guid? indexerId,
        CancellationToken cancellationToken)
    {
        await DeleteExpiredAsync(cancellationToken);

        var video = await context.CatalogueVideos
            .Where(row => row.PrdbId == videoId)
            .Select(row => new { row.Id, row.PrdbId, row.Title })
            .SingleOrDefaultAsync(cancellationToken);
        if (video is null)
        {
            return new(ManualSearchStartOutcome.VideoNotFound, null, "The Video is not in the local Catalogue.");
        }

        var activeId = await ActiveForVideo(video.Id).Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeId is not null)
        {
            return new(ManualSearchStartOutcome.AlreadyRunning, activeId, "A Manual Search for this Video is already active.");
        }

        var indexers = await context.Indexers
            .Where(row => row.Enabled && (indexerId == null || row.Id == indexerId))
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Name)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        if (indexers.Count == 0)
        {
            var outcome = indexerId is null
                ? ManualSearchStartOutcome.NoEnabledIndexers
                : ManualSearchStartOutcome.IndexerNotEnabled;
            return new(outcome, null, indexerId is null
                ? "There are no enabled Indexers."
                : "The selected Indexer does not exist or is disabled.");
        }

        var query = WantedSearchTitle.Of(video.Title);
        if (!WantedSearchTitle.IsSearchable(query))
        {
            return new(ManualSearchStartOutcome.TitleNotSearchable, null,
                "The Video title is too short to make a safe Indexer query.");
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var search = new ManualSearchRow
        {
            Id = id,
            VideoId = video.Id,
            Query = query,
            RequestedAt = now,
        };
        context.ManualSearches.Add(search);
        context.ManualSearchIndexers.AddRange(indexers.Select(selected => new ManualSearchIndexerRow
        {
            SearchId = id,
            IndexerId = selected,
            State = ManualSearchIndexerState.Queued,
        }));
        context.Routines.Add(new RoutineRow
        {
            Name = DiscoveryRoutineNames.ManualSearch,
            Target = Target(id),
            Lane = Lane.Sync,
            DueAt = now,
        });
        await context.SaveChangesAsync(cancellationToken);
        return new(ManualSearchStartOutcome.Started, id, "The Manual Search is queued for the scheduler.");
    }

    public async Task<ManualSearchView?> LatestAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var id = await context.ManualSearches
            .Where(row => row.Video!.PrdbId == videoId)
            .OrderByDescending(row => row.RequestedAt)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id is null ? null : await ReadAsync(id.Value, cancellationToken);
    }

    public async Task<ManualSearchView?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var search = await context.ManualSearches
            .Where(row => row.Id == id)
            .Select(row => new
            {
                row.Id,
                VideoId = row.Video!.PrdbId,
                VideoTitle = row.Video.Title,
                row.Query,
                row.RequestedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (search is null) return null;

        var parts = await context.ManualSearchIndexers
            .Where(row => row.SearchId == id)
            .OrderBy(row => row.Indexer!.Rank)
            .ThenBy(row => row.Indexer!.Name)
            .Select(row => new ManualSearchIndexerView(
                row.IndexerId,
                row.Indexer!.Name,
                row.State,
                row.StartedAt,
                row.FinishedAt,
                row.DeferredUntil,
                row.ResultsSeen,
                row.RowsAdded,
                row.Detail,
                row.State == ManualSearchIndexerState.Failed))
            .ToListAsync(cancellationToken);

        var states = await context.ManualSearchResults
            .Where(row => row.SearchId == id)
            .GroupBy(row => row.Release!.IdentificationState)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.State, row => row.Count, cancellationToken);
        var matches = await context.ManualSearchResults
            .Where(row => row.SearchId == id
                          && row.Release!.IdentificationState == IdentificationState.Matched
                          && row.Release.Video!.PrdbId == search.VideoId)
            .CountAsync(cancellationToken);
        var awaiting = states.GetValueOrDefault(IdentificationState.Awaiting);
        var pending = awaiting + states.GetValueOrDefault(IdentificationState.Unexamined);
        var phase = Phase(parts, pending);
        return new ManualSearchView(
            search.Id,
            search.VideoId,
            search.VideoTitle,
            search.Query,
            search.RequestedAt,
            phase,
            phase is not ManualSearchPhase.Complete and not ManualSearchPhase.Failed,
            parts,
            new ManualSearchResultCounts(
                parts.Sum(part => part.ResultsSeen),
                parts.Sum(part => part.RowsAdded),
                pending,
                awaiting,
                matches,
                states.GetValueOrDefault(IdentificationState.Matched) - matches,
                states.GetValueOrDefault(IdentificationState.Ambiguous),
                states.GetValueOrDefault(IdentificationState.SiteOnly),
                states.GetValueOrDefault(IdentificationState.Unknown),
                states.GetValueOrDefault(IdentificationState.Unremarkable)));
    }

    public async Task<ManualSearchRetryVerdict> RetryAsync(
        Guid searchId,
        Guid indexerId,
        CancellationToken cancellationToken)
    {
        var part = await context.ManualSearchIndexers.AsTracking()
            .SingleOrDefaultAsync(row => row.SearchId == searchId && row.IndexerId == indexerId, cancellationToken);
        if (part is null)
        {
            var exists = await context.ManualSearches.AnyAsync(row => row.Id == searchId, cancellationToken);
            return new(exists ? ManualSearchRetryOutcome.IndexerNotSelected : ManualSearchRetryOutcome.SearchNotFound);
        }
        if (part.State != ManualSearchIndexerState.Failed)
        {
            return new(ManualSearchRetryOutcome.NotRetryable);
        }

        part.State = ManualSearchIndexerState.Queued;
        part.StartedAt = null;
        part.FinishedAt = null;
        part.DeferredUntil = null;
        part.Detail = null;
        await EnsureRoutineAsync(searchId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return new(ManualSearchRetryOutcome.Scheduled);
    }

    private IQueryable<ManualSearchRow> ActiveForVideo(long videoId) =>
        context.ManualSearches
            .Where(row => row.VideoId == videoId
                          && (context.ManualSearchIndexers.Any(part => part.SearchId == row.Id
                              && (part.State == ManualSearchIndexerState.Queued
                                  || part.State == ManualSearchIndexerState.Searching
                                  || part.State == ManualSearchIndexerState.Deferred))
                              || context.ManualSearchResults.Any(result => result.SearchId == row.Id
                                  && (result.Release!.IdentificationState == IdentificationState.Awaiting
                                      || result.Release.IdentificationState == IdentificationState.Unexamined))))
            .OrderByDescending(row => row.RequestedAt);

    private async Task EnsureRoutineAsync(Guid searchId, CancellationToken cancellationToken)
    {
        var target = Target(searchId);
        var now = time.GetUtcNow();
        var madeDue = await context.Routines
            .Where(row => row.Name == DiscoveryRoutineNames.ManualSearch && row.Target == target)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.DueAt, now), cancellationToken);
        if (madeDue == 0)
        {
            context.Routines.Add(new RoutineRow
            {
                Name = DiscoveryRoutineNames.ManualSearch,
                Target = target,
                Lane = Lane.Sync,
                DueAt = now,
            });
        }
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - Retention;
        var ids = await context.ManualSearches.Where(row => row.RequestedAt < cutoff)
            .Select(row => row.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0) return 0;
        var targets = ids.Select(Target).ToArray();
        await context.Routines.Where(row => row.Name == DiscoveryRoutineNames.ManualSearch
                                            && targets.Contains(row.Target!))
            .ExecuteDeleteAsync(cancellationToken);
        return await context.ManualSearches.Where(row => ids.Contains(row.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private static ManualSearchPhase Phase(IReadOnlyList<ManualSearchIndexerView> parts, int awaiting)
    {
        if (parts.Any(part => part.State == ManualSearchIndexerState.Searching)) return ManualSearchPhase.Searching;
        if (parts.Any(part => part.State == ManualSearchIndexerState.Queued)) return ManualSearchPhase.Queued;
        if (parts.Any(part => part.State == ManualSearchIndexerState.Deferred)) return ManualSearchPhase.Deferred;
        if (awaiting > 0) return ManualSearchPhase.Identifying;
        return parts.All(part => part.State == ManualSearchIndexerState.Failed)
            ? ManualSearchPhase.Failed
            : ManualSearchPhase.Complete;
    }

    internal static string Target(Guid id) => id.ToString("D");
}

public sealed record ManualSearchStartVerdict(ManualSearchStartOutcome Outcome, Guid? SearchId, string Detail);
public sealed record ManualSearchRetryVerdict(ManualSearchRetryOutcome Outcome);
public sealed record ManualSearchView(
    Guid Id,
    Guid VideoId,
    string VideoTitle,
    string Query,
    DateTimeOffset RequestedAt,
    ManualSearchPhase Phase,
    bool Active,
    IReadOnlyList<ManualSearchIndexerView> Indexers,
    ManualSearchResultCounts Results);
public sealed record ManualSearchIndexerView(
    Guid IndexerId,
    string Indexer,
    ManualSearchIndexerState State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? DeferredUntil,
    int ResultsSeen,
    int RowsAdded,
    string? Detail,
    bool CanRetry);
public sealed record ManualSearchResultCounts(
    int Seen,
    int Added,
    int Pending,
    int Awaiting,
    int MatchedVideo,
    int MatchedOtherVideo,
    int Ambiguous,
    int SiteOnly,
    int Unknown,
    int Unremarkable);
