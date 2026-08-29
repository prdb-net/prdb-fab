using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>The three reason-bound Review Queue actions.</summary>
public sealed class ReviewDecisions(
    FabDbContext context,
    PrdbGateway prdb,
    VideoDetails details)
{
    public async Task<ReviewDecisionVerdict> FileAsAsync(
        Guid arrivingFileId,
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var arrival = await OpenAsync(arrivingFileId, ArrivingFileReason.Unidentified, cancellationToken);
        if (arrival is null) return Changed();

        var installation = await context.Installation
            .AsNoTracking()
            .Select(row => new { row.PrdbApiKey, row.PrdbUserHash })
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(installation.PrdbApiKey)
            || string.IsNullOrWhiteSpace(installation.PrdbUserHash)
            || string.IsNullOrWhiteSpace(arrival.OsHash))
        {
            return new ReviewDecisionVerdict(
                ReviewDecisionOutcome.CannotAct,
                "The selected file cannot create the required Confirmed Assignment.");
        }

        var detail = await prdb.AskAsync(
            installation.PrdbApiKey,
            PrdbWork.Identification,
            (client, token) => client.Videos[videoId].GetAsync(cancellationToken: token),
            cancellationToken);
        if (detail?.Id != videoId)
        {
            return new ReviewDecisionVerdict(
                ReviewDecisionOutcome.VideoNotFound,
                "prdb no longer returns the selected Video.");
        }

        await details.WriteAsync(detail, cancellationToken);

        var releaseName = await context.Releases
            .AsNoTracking()
            .Where(row => row.IndexerId == arrival.IndexerId
                && row.DerivedReleaseId == arrival.DerivedReleaseId)
            .Select(row => row.Title)
            .SingleOrDefaultAsync(cancellationToken)
            ?? await context.Downloads
                .Where(row => row.Id == arrival.DownloadId)
                .Select(row => row.SubmittedName)
                .SingleAsync(cancellationToken);
        var assignment = await context.ConfirmedAssignments
            .AsTracking()
            .SingleOrDefaultAsync(row => row.OsHash == arrival.OsHash
                && row.VideoId == videoId
                && row.UserHash == installation.PrdbUserHash,
                cancellationToken);
        if (assignment is null)
        {
            context.ConfirmedAssignments.Add(new ConfirmedAssignmentRow
            {
                OsHash = arrival.OsHash,
                VideoId = videoId,
                UserHash = installation.PrdbUserHash,
                SizeBytes = arrival.SizeBytes,
                ArrivalFileName = arrival.ArrivedName,
                ReleaseName = releaseName,
                RuntimeSeconds = arrival.RuntimeSeconds,
                Width = arrival.Width,
                Height = arrival.Height,
                VideoCodec = arrival.VideoCodec,
            });
        }

        await context.ArrivingFileCandidates
            .Where(row => row.ArrivingFileId == arrival.Id)
            .ExecuteDeleteAsync(cancellationToken);
        arrival.VideoId = videoId;
        arrival.SiteId = null;
        arrival.Reason = null;
        arrival.State = ArrivingFileState.AwaitingFiling;
        arrival.IntendedPath = null;
        await context.SaveChangesAsync(cancellationToken);

        return new ReviewDecisionVerdict(
            ReviewDecisionOutcome.QueuedForFiling,
            "The Confirmed Assignment was recorded and the file returned to ordinary Filing checks.");
    }

    public async Task<ReviewDecisionVerdict> FileAsOnlyCopyAsync(
        Guid arrivingFileId,
        CancellationToken cancellationToken = default)
    {
        var arrival = await OpenAsync(arrivingFileId, ArrivingFileReason.EntryMissing, cancellationToken);
        if (arrival?.VideoId is not { } videoId) return Changed();

        var entry = await context.LibraryEntries
            .AsTracking()
            .SingleOrDefaultAsync(row => row.VideoId == videoId, cancellationToken);
        if (entry is null) return Changed();

        context.LibraryEntries.Remove(entry);
        arrival.Reason = null;
        arrival.State = ArrivingFileState.AwaitingFiling;
        arrival.IntendedPath = null;
        await context.SaveChangesAsync(cancellationToken);

        return new ReviewDecisionVerdict(
            ReviewDecisionOutcome.QueuedForFiling,
            "The missing Library Entry record was cleared and the arriving file will be filed as the only copy.");
    }

    public async Task<ReviewDecisionVerdict> ReplaceAsync(
        Guid arrivingFileId,
        CancellationToken cancellationToken = default)
    {
        var arrival = await OpenAsync(arrivingFileId, ArrivingFileReason.Duplicate, cancellationToken);
        if (arrival?.VideoId is not { } videoId || arrival.QualityLabel is not { } quality)
        {
            return Changed();
        }

        var filed = await context.VideoFiles
            .AsNoTracking()
            .Where(row => row.LibraryEntryVideoId == videoId && row.QualityLabel == quality)
            .OrderBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (filed is null || !File.Exists(filed.FiledPath))
        {
            return Changed("The filed Video File is no longer present; nothing was replaced.");
        }

        var entryDirectory = Path.GetDirectoryName(filed.FiledPath)!;
        var name = EntryPath.At(entryDirectory, Path.GetExtension(arrival.SourcePath));
        var labelled = Path.GetFileNameWithoutExtension(filed.FiledPath)
            .EndsWith($" - [{quality}]", StringComparison.Ordinal);
        var intended = Path.Combine(
            entryDirectory,
            labelled ? name.VideoFileNameFor(quality) : name.VideoFileName);
        if (!string.Equals(intended, filed.FiledPath, StringComparison.Ordinal)
            && File.Exists(intended))
        {
            return new ReviewDecisionVerdict(
                ReviewDecisionOutcome.CannotAct,
                "The replacement path is occupied by different content; nothing was replaced.");
        }

        // Durable permission. The serial file lane performs and resumes the
        // copy, so a browser request never owns an hour-long filesystem act.
        arrival.State = ArrivingFileState.Filing;
        arrival.IntendedPath = intended;
        await context.SaveChangesAsync(cancellationToken);
        return new ReviewDecisionVerdict(
            ReviewDecisionOutcome.QueuedForReplacement,
            "Replacement was confirmed and queued in the serial File lane.");
    }

    private Task<ArrivingFileRow?> OpenAsync(
        Guid id,
        ArrivingFileReason reason,
        CancellationToken cancellationToken) =>
        context.ArrivingFiles
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == id && row.Reason == reason, cancellationToken);

    private static ReviewDecisionVerdict Changed(string? detail = null) => new(
        ReviewDecisionOutcome.SelectionChanged,
        detail ?? "The Review Queue entry changed; nothing was changed.");
}

public enum ReviewDecisionOutcome
{
    SelectionChanged,
    CannotAct,
    VideoNotFound,
    QueuedForFiling,
    QueuedForReplacement,
}

public sealed record ReviewDecisionVerdict(ReviewDecisionOutcome Outcome, string Detail);
