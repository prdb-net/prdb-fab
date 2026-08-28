using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>The local Download record and its two local-only actions.</summary>
public sealed class DownloadBrowse(FabDbContext context)
{
    public const int APage = 50;

    public async Task<DownloadPage> ReadAsync(
        DownloadState? state,
        Guid? indexerId,
        int page,
        CancellationToken cancellationToken = default)
    {
        var wanted = Math.Max(page, 1);
        var relevant = context.Downloads.AsNoTracking();
        var indexers = await context.Downloads
            .AsNoTracking()
            .Join(context.Indexers, download => download.IndexerId, indexer => indexer.Id, (_, indexer) => indexer)
            .Distinct()
            .OrderBy(indexer => indexer.Name)
            .ThenBy(indexer => indexer.Id)
            .Select(indexer => new DownloadIndexer(indexer.Id, indexer.Name))
            .ToListAsync(cancellationToken);

        if (state is not null) relevant = relevant.Where(row => row.State == state);
        if (indexerId is not null) relevant = relevant.Where(row => row.IndexerId == indexerId);

        var total = await relevant.CountAsync(cancellationToken);
        var rows = await relevant
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Skip((wanted - 1) * APage)
            .Take(APage)
            .Select(row => new
            {
                row.Id,
                row.VideoId,
                VideoTitle = context.CatalogueVideos
                    .Where(video => video.PrdbId == row.VideoId)
                    .Select(video => video.Title)
                    .SingleOrDefault(),
                row.IndexerId,
                IndexerName = context.Indexers
                    .Where(indexer => indexer.Id == row.IndexerId)
                    .Select(indexer => indexer.Name)
                    .SingleOrDefault(),
                row.DerivedReleaseId,
                row.SubmittedName,
                Size = context.Releases
                    .Where(release => release.IndexerId == row.IndexerId
                        && release.DerivedReleaseId == row.DerivedReleaseId)
                    .Select(release => release.Size)
                    .SingleOrDefault(),
                row.NzoId,
                row.State,
                row.Cause,
                row.LastSabnzbdStatus,
                row.FailMessage,
                row.StageLog,
                row.OutstandingSince,
                row.OriginIsPerson,
                row.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new(
            [.. rows.Select(row => new DownloadViewRow(
                row.Id,
                row.VideoId,
                row.VideoTitle ?? "Unknown Video",
                new DownloadIndexer(row.IndexerId, row.IndexerName ?? "Unknown Indexer"),
                row.DerivedReleaseId,
                row.SubmittedName,
                row.Size,
                row.NzoId,
                row.State,
                row.Cause,
                row.LastSabnzbdStatus,
                row.FailMessage,
                row.StageLog,
                row.OutstandingSince,
                row.OriginIsPerson ? DownloadOrigin.Person : DownloadOrigin.Automation,
                row.CreatedAt))],
            indexers,
            wanted,
            APage,
            total);
    }

    public async Task<DownloadSelectionPreview> PreviewStopFollowingAsync(
        IReadOnlyCollection<Guid> downloadIds,
        CancellationToken cancellationToken = default)
    {
        var ids = downloadIds.Distinct().ToArray();
        var downloads = await SelectionAsync(ids, cancellationToken);
        var outcome = ids.Length > 0
            && downloads.Count == ids.Length
            && downloads.All(row => row.State == DownloadState.Outstanding)
                ? DownloadSelectionOutcome.Ready
                : DownloadSelectionOutcome.SelectionChanged;
        return new(outcome, downloads, DetailOf(outcome));
    }

    public async Task<DownloadSelectionVerdict> StopFollowingAsync(
        IReadOnlyCollection<Guid> downloadIds,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewStopFollowingAsync(downloadIds, cancellationToken);
        if (preview.Outcome != DownloadSelectionOutcome.Ready)
        {
            return new(preview.Outcome, 0, preview.Detail);
        }

        var ids = preview.Downloads.Select(row => row.Id).ToArray();
        var changed = await context.Downloads
            .Where(row => ids.Contains(row.Id)
                && row.State == DownloadState.Outstanding
                && context.Downloads.Count(candidate => ids.Contains(candidate.Id)
                    && candidate.State == DownloadState.Outstanding) == ids.Length)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.State, DownloadState.Failed)
                .SetProperty(row => row.Cause, DownloadCause.Abandoned), cancellationToken);

        return changed == ids.Length
            ? new(DownloadSelectionOutcome.Stopped, changed,
                "Following stopped locally. SABnzbd was not changed.")
            : new(DownloadSelectionOutcome.SelectionChanged, 0,
                "The selection changed before the action; nothing was stopped.");
    }

    public async Task<DownloadResetPreview> PreviewResetAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var downloads = await context.Downloads
            .AsNoTracking()
            .Where(row => row.VideoId == videoId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => new DownloadSelectionRow(
                row.Id,
                row.VideoId,
                row.SubmittedName,
                row.State,
                row.Cause,
                row.NzoId))
            .ToListAsync(cancellationToken);
        return new(
            downloads.Count == 0 ? DownloadResetOutcome.NothingToReset : DownloadResetOutcome.Ready,
            videoId,
            downloads,
            downloads.Count == 0
                ? "This Video has no Download history to reset."
                : "This exact Video's local Download history will be deleted. SABnzbd will not be changed.");
    }

    public async Task<DownloadResetVerdict> ResetAsync(
        Guid videoId,
        IReadOnlyCollection<Guid> downloadIds,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewResetAsync(videoId, cancellationToken);
        if (preview.Outcome != DownloadResetOutcome.Ready)
        {
            return new(preview.Outcome, 0, preview.Detail);
        }

        var expected = preview.Downloads.Select(row => row.Id).Order().ToArray();
        var supplied = downloadIds.Distinct().Order().ToArray();
        if (!expected.SequenceEqual(supplied))
        {
            return new(
                DownloadResetOutcome.SelectionChanged,
                0,
                "The Download history changed after the preview; nothing was reset.");
        }

        var removed = await context.Downloads
            .Where(row => row.VideoId == videoId
                && supplied.Contains(row.Id)
                && context.Downloads.Count(candidate => candidate.VideoId == videoId) == supplied.Length)
            .ExecuteDeleteAsync(cancellationToken);
        return removed == supplied.Length
            ? new(DownloadResetOutcome.Reset, removed,
                "The local Download history for this Video was reset. SABnzbd was not changed.")
            : new(DownloadResetOutcome.SelectionChanged, 0,
                "The Download history changed during the action; nothing was reset.");
    }

    public async Task<IReadOnlyList<DownloadSelectionRow>> ForVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken = default) =>
        await context.Downloads
            .AsNoTracking()
            .Where(row => row.VideoId == videoId)
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => new DownloadSelectionRow(
                row.Id,
                row.VideoId,
                row.SubmittedName,
                row.State,
                row.Cause,
                row.NzoId))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<DownloadSelectionRow>> SelectionAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await context.Downloads
            .AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .OrderByDescending(row => row.CreatedAt)
            .Select(row => new DownloadSelectionRow(
                row.Id,
                row.VideoId,
                row.SubmittedName,
                row.State,
                row.Cause,
                row.NzoId))
            .ToListAsync(cancellationToken);

    private static string DetailOf(DownloadSelectionOutcome outcome) => outcome switch
    {
        DownloadSelectionOutcome.Ready => "Following will stop locally for these Downloads. SABnzbd will not be changed.",
        _ => "Every selected Download must still be Outstanding; nothing was changed.",
    };
}

public enum DownloadOrigin { Person, Automation }
public enum DownloadSelectionOutcome { Ready, SelectionChanged, Stopped }
public enum DownloadResetOutcome { Ready, NothingToReset, SelectionChanged, Reset }

public sealed record DownloadIndexer(Guid Id, string Name);
public sealed record DownloadViewRow(
    Guid Id,
    Guid VideoId,
    string VideoTitle,
    DownloadIndexer Indexer,
    string DerivedReleaseId,
    string SubmittedName,
    long? Size,
    string? NzoId,
    DownloadState State,
    DownloadCause? Cause,
    string? LastSabnzbdStatus,
    string? FailMessage,
    string? StageLog,
    DateTimeOffset OutstandingSince,
    DownloadOrigin Origin,
    DateTimeOffset CreatedAt);
public sealed record DownloadPage(
    IReadOnlyList<DownloadViewRow> Downloads,
    IReadOnlyList<DownloadIndexer> Indexers,
    int Page,
    int PageSize,
    int Total);
public sealed record DownloadSelectionRow(
    Guid Id,
    Guid VideoId,
    string SubmittedName,
    DownloadState State,
    DownloadCause? Cause,
    string? NzoId);
public sealed record DownloadSelectionPreview(
    DownloadSelectionOutcome Outcome,
    IReadOnlyList<DownloadSelectionRow> Downloads,
    string Detail);
public sealed record DownloadSelectionVerdict(DownloadSelectionOutcome Outcome, int Changed, string Detail);
public sealed record DownloadResetPreview(
    DownloadResetOutcome Outcome,
    Guid VideoId,
    IReadOnlyList<DownloadSelectionRow> Downloads,
    string Detail);
public sealed record DownloadResetVerdict(DownloadResetOutcome Outcome, int Removed, string Detail);
