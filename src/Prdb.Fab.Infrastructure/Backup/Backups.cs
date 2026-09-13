using System.Reflection;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Backup;

/// <summary>
/// Builds the Backup document out of the tables that cross ADR 0033's boundary.
/// </summary>
/// <remarks>
/// <para>
/// Reads only. ADR 0009 needs nothing quiesced for this — everything in the file
/// is committed state, and the one thing a running sync writes heavily, the
/// Indexer Cache, is not in it.
/// </para>
/// <para>
/// Two transformations happen here and nowhere else, both because ADR 0033 puts
/// them in the document rather than in the database: an absolute path becomes
/// root-relative, and the Catalogue's local surrogate for the What's New marker
/// becomes prdb's own Video id.
/// </para>
/// </remarks>
public sealed class Backups(FabDbContext context, TimeProvider time)
{
    /// <summary>
    /// What this build calls itself, which ADR 0009 needs so that a Restore can
    /// refuse a document from a newer tool by name. The same attribute ADR
    /// 0044's first log line reads, taken from this assembly because every
    /// project in the build carries the one version.
    /// </summary>
    private static readonly string ToolVersion = typeof(Backups).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";

    public async Task<BackupDocument> ReadAsync(CancellationToken cancellationToken = default)
    {
        // Before anything is read, so that a table added without a section is a
        // refusal rather than a Backup with a hole in it.
        BackupSections.MustCover(context.Model);

        // One committed view, which is the whole of what a transaction is for
        // here. Sixteen separate reads could otherwise straddle a lane's write
        // and produce a document holding an Arriving File whose Download is not
        // in it — and a Restore would then have nowhere to hang the row.
        //
        // It costs the lanes nothing. ADR 0039 opens SQLite in WAL, where a
        // deferred read transaction takes its snapshot at the first read and
        // never blocks a writer, which is why ADR 0009 could say that nothing
        // has to be quiesced for an export.
        await using var view = await context.Database.BeginTransactionAsync(cancellationToken);

        var installation = await context.Installation
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var roots = new BackupRoots(installation.LibraryRoot, installation.PathMappingTo);
        var observedVideo = installation.WhatsNewObservedVideoId is { } observed
            ? await context.CatalogueVideos
                .AsNoTracking()
                .Where(row => row.Id == observed)
                .Select(row => (Guid?)row.PrdbId)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        // Ordered by what identifies each row, so that two Backups of the same
        // installation are the same document and a person can diff them.
        var gateAdmissions = await context.GateAdmissions
            .AsNoTracking()
            .OrderBy(row => row.Gate)
            .ThenBy(row => row.Confidence)
            .Select(row => new BackupGateAdmission(row.Gate, row.Confidence))
            .ToListAsync(cancellationToken);

        var indexers = await context.Indexers
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new BackupIndexer(
                row.Id,
                row.Name,
                row.Url,
                row.ApiKey,
                row.Categories,
                row.LastVerdict,
                row.LastCheckedAt,
                row.Enabled,
                row.Rank,
                row.DailyQueryBudget))
            .ToListAsync(cancellationToken);

        var automationRules = await context.AutomationRules
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new BackupAutomationRule(
                row.Id,
                row.Name,
                row.Enabled,
                row.MinimumSize,
                row.MaximumSize))
            .ToListAsync(cancellationToken);

        var automationRuleIndexers = await context.AutomationRuleIndexers
            .AsNoTracking()
            .OrderBy(row => row.AutomationRuleId)
            .ThenBy(row => row.IndexerId)
            .Select(row => new BackupAutomationRuleIndexer(row.AutomationRuleId, row.IndexerId))
            .ToListAsync(cancellationToken);

        var libraryEntries = await context.LibraryEntries
            .AsNoTracking()
            .OrderBy(row => row.VideoId)
            .Select(row => new { row.VideoId, row.EntryDirectory, row.FiledAt })
            .ToListAsync(cancellationToken);

        var videoFiles = await context.VideoFiles
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var downloads = await context.Downloads
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new BackupDownload(
                row.Id,
                row.VideoId,
                row.IndexerId,
                row.DerivedReleaseId,
                row.SubmittedName,
                row.NzoId,
                row.SubmissionState,
                row.State,
                row.Cause,
                row.LastSabnzbdStatus,
                row.FailMessage,
                row.StageLog,
                row.Storage,
                row.ConsecutiveAbsences,
                row.OutstandingSince,
                row.TidiedAt,
                row.OriginIsPerson,
                row.CreatedAt))
            .ToListAsync(cancellationToken);

        var downloadOriginRules = await context.DownloadOriginRules
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new BackupDownloadOriginRule(
                row.Id,
                row.DownloadId,
                row.AutomationRuleId,
                row.RuleName))
            .ToListAsync(cancellationToken);

        var arrivingFiles = await context.ArrivingFiles
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var arrivingFileCandidates = await context.ArrivingFileCandidates
            .AsNoTracking()
            .OrderBy(row => row.ArrivingFileId)
            .ThenBy(row => row.VideoId)
            .Select(row => new BackupArrivingFileCandidate(row.ArrivingFileId, row.VideoId))
            .ToListAsync(cancellationToken);

        var reportedStates = await context.ReportedStates
            .AsNoTracking()
            .OrderBy(row => row.VideoId)
            .ThenBy(row => row.UserHash)
            .Select(row => new BackupReportedState(
                row.VideoId,
                row.UserHash,
                row.IsFulfilled,
                row.Quality,
                row.FulfilledAt,
                row.TerminalOutcome))
            .ToListAsync(cancellationToken);

        var confirmedAssignments = await context.ConfirmedAssignments
            .AsNoTracking()
            .OrderBy(row => row.OsHash)
            .ThenBy(row => row.VideoId)
            .ThenBy(row => row.UserHash)
            .Select(row => new BackupConfirmedAssignment(
                row.OsHash,
                row.VideoId,
                row.UserHash,
                row.SizeBytes,
                row.ArrivalFileName,
                row.ReleaseName,
                row.RuntimeSeconds,
                row.Width,
                row.Height,
                row.VideoCodec,
                row.PrdbAnswer,
                row.SentAt))
            .ToListAsync(cancellationToken);

        var operationLog = await context.OperationLogEntries
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var identificationFlags = await context.IdentificationFlags
            .AsNoTracking()
            .OrderBy(row => row.VideoFileId)
            .Select(row => new BackupIdentificationFlag(
                row.VideoFileId,
                row.VideoId,
                row.NowNamesVideoId,
                row.Reason,
                row.At))
            .ToListAsync(cancellationToken);

        var accountPreferenceWrites = await context.AccountPreferenceWrites
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new BackupAccountPreferenceWrite(
                row.Id,
                row.Kind,
                row.EntityId,
                row.Desired,
                row.RequestedAt,
                row.LastFailure,
                row.Blocked))
            .ToListAsync(cancellationToken);

        return new BackupDocument(
            BackupFormat.Version,
            ToolVersion,
            time.GetUtcNow(),
            new BackupInstallation(
                installation.PasswordHash,
                installation.PrdbApiKey,
                installation.PrdbUserHash,
                installation.LibraryRoot,
                installation.SabnzbdUrl,
                installation.SabnzbdApiKey,
                installation.SabnzbdCategory,
                installation.PathMappingFrom,
                installation.PathMappingTo,
                installation.OnboardingStep,
                installation.SabnzbdSkipped,
                installation.IndexersSkipped,
                installation.PlanShortSince,
                installation.RetryBudget,
                installation.PreferredDownloadQuality,
                installation.AutomaticDownloadCap,
                installation.DeleteLeftovers,
                installation.ReportFulfilments,
                installation.ReportConfirmedAssignments,
                installation.WhatsNewObservedAt,
                observedVideo),
            gateAdmissions,
            indexers,
            automationRules,
            automationRuleIndexers,
            [.. libraryEntries.Select(row => new BackupLibraryEntry(
                row.VideoId,
                roots.Relative(row.EntryDirectory),
                row.FiledAt))],
            [.. videoFiles.Select(row => new BackupVideoFile(
                row.Id,
                row.LibraryEntryVideoId,
                roots.Relative(row.FiledPath),
                row.QualityLabel,
                row.SizeBytes,
                row.RuntimeSeconds,
                row.Width,
                row.Height,
                row.VideoCodec,
                row.OsHash))],
            downloads,
            downloadOriginRules,
            [.. arrivingFiles.Select(row => new BackupArrivingFile(
                row.Id,
                row.DownloadId,
                row.IndexerId,
                row.DerivedReleaseId,
                roots.Relative(row.SourcePath),
                row.ArrivedName,
                row.IsOnDisk,
                row.State,
                row.Reason,
                row.VideoId,
                row.SiteId,
                row.SizeBytes,
                row.RuntimeSeconds,
                row.Width,
                row.Height,
                row.VideoCodec,
                row.QualityLabel,
                row.OsHash,
                row.IntendedPath is null ? null : roots.Relative(row.IntendedPath),
                row.LastAttemptedAt,
                row.Confidence,
                row.MatchedBy,
                row.ProbeOutcome,
                row.ProbeError))],
            arrivingFileCandidates,
            reportedStates,
            confirmedAssignments,
            [.. operationLog.Select(row => new BackupOperationLogEntry(
                row.Id,
                row.Act,
                row.VideoFileId,
                row.LibraryEntryVideoId,
                row.VideoId,
                row.DownloadId,
                row.PathBefore is null ? null : roots.Relative(row.PathBefore),
                row.PathAfter is null ? null : roots.Relative(row.PathAfter),
                row.DisplacedPath is null ? null : roots.Relative(row.DisplacedPath),
                row.LeftoverNamesJson,
                row.Actor,
                row.Reason,
                row.At))],
            accountPreferenceWrites,
            identificationFlags);
    }
}
