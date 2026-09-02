using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>The Review Queue and the two universal exits from it.</summary>
public sealed class ReviewQueue(FabDbContext context, TimeProvider time)
{
    public const int APage = 50;

    public async Task<ReviewQueuePage> ReadAsync(
        ArrivingFileReason? reason,
        Guid? downloadId,
        int page,
        CancellationToken cancellationToken = default)
    {
        var wanted = Paging.Wanted(page);
        var open = context.ArrivingFiles
            .AsNoTracking()
            .Where(row => row.Reason != null);

        if (reason is not null) open = open.Where(row => row.Reason == reason);
        if (downloadId is not null) open = open.Where(row => row.DownloadId == downloadId);

        var total = await open.CountAsync(cancellationToken);
        var globalCount = await context.ArrivingFiles.CountAsync(
            row => row.Reason != null,
            cancellationToken);
        var arrivals = await open
            .OrderByDescending(row => row.Id)
            .Skip(Paging.Skip(wanted, APage))
            .Take(APage)
            .ToListAsync(cancellationToken);

        var entries = new List<ReviewQueueEntry>(arrivals.Count);
        foreach (var arrival in arrivals)
        {
            entries.Add(await EntryAsync(arrival, cancellationToken));
        }

        return new ReviewQueuePage(entries, wanted, APage, total, globalCount);
    }

    public async Task<ReviewSelectionPreview> PreviewAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct().ToArray();
        var rows = await context.ArrivingFiles
            .AsNoTracking()
            .Where(row => distinct.Contains(row.Id) && row.Reason != null)
            .OrderBy(row => row.ArrivedName)
            .Select(row => new ReviewSelectionFile(
                row.Id,
                row.ArrivedName,
                row.SourcePath,
                row.SizeBytes,
                row.Reason!.Value,
                row.IsOnDisk))
            .ToListAsync(cancellationToken);
        var ready = distinct.Length > 0 && rows.Count == distinct.Length;

        return new ReviewSelectionPreview(
            ready ? ReviewSelectionOutcome.Ready : ReviewSelectionOutcome.SelectionChanged,
            rows,
            ready
                ? "Every selected Video File is still open in the Review Queue."
                : "The Review Queue selection changed; nothing was changed.");
    }

    public async Task<ReviewSelectionVerdict> DeleteAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(ids, cancellationToken);
        if (preview.Outcome != ReviewSelectionOutcome.Ready)
        {
            return new ReviewSelectionVerdict(preview.Outcome, 0, preview.Detail);
        }

        var selected = preview.Files.Select(file => file.Id).ToArray();
        var rows = await context.ArrivingFiles
            .AsTracking()
            .Where(row => selected.Contains(row.Id) && row.Reason != null)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count != selected.Length)
        {
            return Changed();
        }

        foreach (var row in rows)
        {
            if (!File.Exists(row.SourcePath)
                || new FileInfo(row.SourcePath).Length != row.SizeBytes)
            {
                row.IsOnDisk = false;
                await context.SaveChangesAsync(cancellationToken);
                return Changed("A selected Video File is no longer present at the confirmed size; nothing else was deleted.");
            }
        }

        foreach (var row in rows)
        {
            File.Delete(row.SourcePath);
            var now = time.GetUtcNow();
            context.OperationLogEntries.Add(new OperationLogEntryRow
            {
                Id = Guid.CreateVersion7(now),
                Act = "Deleted",
                VideoId = row.VideoId,
                DownloadId = row.DownloadId,
                PathBefore = row.SourcePath,
                Actor = "Person",
                Reason = $"Review Queue: {row.Reason}",
                At = now,
            });
            context.ArrivingFiles.Remove(row);
            await context.SaveChangesAsync(cancellationToken);
        }

        return new ReviewSelectionVerdict(
            ReviewSelectionOutcome.Deleted,
            rows.Count,
            $"Deleted {rows.Count} confirmed Video File(s).");
    }

    public async Task<ReviewSelectionVerdict> DismissAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(ids, cancellationToken);
        if (preview.Outcome != ReviewSelectionOutcome.Ready)
        {
            return new ReviewSelectionVerdict(preview.Outcome, 0, preview.Detail);
        }

        var selected = preview.Files.Select(file => file.Id).ToArray();
        var removed = await context.ArrivingFiles
            .Where(row => selected.Contains(row.Id)
                && row.Reason != null
                && context.ArrivingFiles.Count(candidate =>
                    selected.Contains(candidate.Id) && candidate.Reason != null) == selected.Length)
            .ExecuteDeleteAsync(cancellationToken);

        return removed == selected.Length
            ? new ReviewSelectionVerdict(
                ReviewSelectionOutcome.Dismissed,
                removed,
                "The entries were dismissed. Their Video Files were left exactly where they were.")
            : Changed();
    }

    private async Task<ReviewQueueEntry> EntryAsync(
        ArrivingFileRow arrival,
        CancellationToken cancellationToken)
    {
        var download = await context.Downloads
            .AsNoTracking()
            .Where(row => row.Id == arrival.DownloadId)
            .Select(row => new ReviewDownload(row.Id, row.SubmittedName))
            .SingleAsync(cancellationToken);
        var indexer = await context.Indexers
            .AsNoTracking()
            .Where(row => row.Id == arrival.IndexerId)
            .Select(row => row.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Unknown Indexer";
        var release = await context.Releases
            .AsNoTracking()
            .Where(row => row.IndexerId == arrival.IndexerId
                && row.DerivedReleaseId == arrival.DerivedReleaseId)
            .Select(row => row.Title)
            .SingleOrDefaultAsync(cancellationToken) ?? download.Name;
        var video = await VideoAsync(arrival.VideoId, cancellationToken);
        var candidates = await context.ArrivingFileCandidates
            .AsNoTracking()
            .Where(row => row.ArrivingFileId == arrival.Id)
            .Join(
                context.CatalogueVideos,
                candidate => candidate.VideoId,
                candidate => candidate.PrdbId,
                (_, candidate) => candidate)
            .OrderBy(row => row.Title)
            .Select(row => new ReviewVideo(
                row.PrdbId,
                row.Title,
                row.Site == null ? null : row.Site.Title,
                row.ReleaseDate,
                row.DurationMs,
                row.DurationFileCount,
                row.Id))
            .ToListAsync(cancellationToken);
        var filed = await FiledComparisonAsync(arrival, cancellationToken);

        return new ReviewQueueEntry(
            arrival.Id,
            arrival.Reason!.Value,
            ReviewQueueActions.For(arrival.Reason.Value),
            arrival.ArrivedName,
            arrival.SourcePath,
            arrival.IsOnDisk,
            arrival.SizeBytes,
            arrival.RuntimeSeconds,
            arrival.QualityLabel,
            arrival.Width,
            arrival.Height,
            arrival.VideoCodec,
            arrival.ProbeError,
            arrival.Confidence,
            arrival.MatchedBy,
            video,
            candidates,
            filed,
            download,
            release,
            indexer);
    }

    private async Task<ReviewVideo?> VideoAsync(Guid? videoId, CancellationToken cancellationToken)
    {
        if (videoId is null) return null;

        return await context.CatalogueVideos
            .AsNoTracking()
            .Where(row => row.PrdbId == videoId)
            .Select(row => new ReviewVideo(
                row.PrdbId,
                row.Title,
                row.Site == null ? null : row.Site.Title,
                row.ReleaseDate,
                row.DurationMs,
                row.DurationFileCount,
                row.Id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<ReviewFiledFile?> FiledComparisonAsync(
        ArrivingFileRow arrival,
        CancellationToken cancellationToken)
    {
        IQueryable<VideoFileRow> files = context.VideoFiles.AsNoTracking();
        if (arrival.Reason == ArrivingFileReason.IdenticalFile && arrival.OsHash is { } hash)
        {
            files = files.Where(row => row.OsHash == hash);
        }
        else if (arrival.VideoId is { } videoId && arrival.QualityLabel is { } quality)
        {
            files = files.Where(row => row.LibraryEntryVideoId == videoId && row.QualityLabel == quality);
        }
        else
        {
            return null;
        }

        return await files
            .OrderBy(row => row.Id)
            .Select(row => new ReviewFiledFile(row.FiledPath, row.QualityLabel, row.SizeBytes))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ReviewSelectionVerdict Changed(string? detail = null) => new(
        ReviewSelectionOutcome.SelectionChanged,
        0,
        detail ?? "The Review Queue selection changed; nothing was changed.");
}

public enum ReviewSelectionOutcome { Ready, SelectionChanged, Deleted, Dismissed }

public sealed record ReviewSelectionFile(
    Guid Id,
    string FileName,
    string Path,
    long SizeBytes,
    ArrivingFileReason Reason,
    bool IsOnDisk);

public sealed record ReviewSelectionPreview(
    ReviewSelectionOutcome Outcome,
    IReadOnlyList<ReviewSelectionFile> Files,
    string Detail);

public sealed record ReviewSelectionVerdict(ReviewSelectionOutcome Outcome, int Changed, string Detail);

public sealed record ReviewDownload(Guid Id, string Name);
public sealed record ReviewFiledFile(string Path, string Quality, long SizeBytes);
public sealed record ReviewVideo(
    Guid Id,
    string Title,
    string? Site,
    DateOnly? ReleaseDate,
    long? ConsensusRuntimeMs = null,
    int? ConsensusRuntimeFileCount = null,
    long? ArtworkId = null);

public sealed record ReviewQueueEntry(
    Guid Id,
    ArrivingFileReason Reason,
    ReviewQueueAction? ActingAction,
    string FileName,
    string Path,
    bool IsOnDisk,
    long SizeBytes,
    long? RuntimeSeconds,
    string? Quality,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? ProbeError,
    Core.ReleaseDiscovery.IdentificationConfidence? Confidence,
    Core.ReleaseDiscovery.IdentificationRung? MatchedBy,
    ReviewVideo? Video,
    IReadOnlyList<ReviewVideo> Candidates,
    ReviewFiledFile? FiledFile,
    ReviewDownload Download,
    string Release,
    string Indexer);

public sealed record ReviewQueuePage(
    IReadOnlyList<ReviewQueueEntry> Entries,
    int Page,
    int PageSize,
    int Total,
    int GlobalCount);
