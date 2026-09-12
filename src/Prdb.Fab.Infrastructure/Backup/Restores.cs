using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Backup;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Backup;

/// <summary>
/// The two roots a Restore is answered with, as the person gave them.
/// </summary>
/// <remarks>
/// Null for the whole record means they have not been asked yet, which is what
/// makes a Restore two calls to one endpoint rather than two endpoints: the
/// first hands back what the file recorded, the second carries what the person
/// decided. Null <em>inside</em> it is an answer — "there is no download
/// directory here" — and is refused only where the document needs one.
/// </remarks>
public sealed record RestoreRoots(string? Library, string? Downloads);

/// <param name="Found">
/// What made the installation non-empty, one sentence each. ADR 0009 refuses by
/// naming, because "not empty" on its own is a dead end for whoever is holding
/// the file.
/// </param>
/// <param name="Refusal">Why a root was refused, where one was.</param>
/// <param name="RefusedRoot">Which root that was.</param>
public sealed record RestoreAct(
    RestoreOutcome Outcome,
    string Detail,
    BackupSummary? Summary = null,
    IReadOnlyList<string>? Found = null,
    RootRefusal? Refusal = null,
    BackupRoot? RefusedRoot = null,
    string? WrittenBy = null);

/// <param name="NeedsLibraryRoot">
/// Whether anything in the document is recorded under the Library — including
/// the Library root the Installation itself carries, which is the one an
/// installation that never filed anything still has.
/// </param>
/// <param name="RecordedLibraryRoot">
/// Where it was on the machine that wrote the file, so the question can be
/// prefilled rather than asked blind. ADR 0009 wants it re-answered, not
/// reused: the container reading this may mount its library somewhere else.
/// </param>
public sealed record BackupSummary(
    int FormatVersion,
    string ToolVersion,
    DateTimeOffset WrittenAt,
    bool NeedsLibraryRoot,
    bool NeedsDownloadDirectory,
    string? RecordedLibraryRoot,
    string? RecordedDownloadDirectory,
    int Indexers,
    int AutomationRules,
    int LibraryEntries,
    int VideoFiles,
    int Downloads,
    int ArrivingFiles);

/// <summary>
/// ADR 0009's Restore: a Backup becomes this installation, on an installation
/// that holds nothing yet, in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal happens before the transaction opens. That is not tidiness —
/// it is the ticket's requirement that a malformed, mis-rooted or non-empty
/// Restore changes nothing, and the cheapest way to hold it is to have written
/// nothing to change.
/// </para>
/// <para>
/// The order rows are written in is the order the foreign keys allow: an
/// Automation Rule before the Indexers it permits, a Download before the
/// Arriving File that came out of it. The references that leave the boundary —
/// a Video id, a Site id, ADR 0015's derived Release identity — are nobody's
/// foreign key here, which is exactly what ADR 0033 bought by insisting they be
/// identifiers an outside authority owns.
/// </para>
/// </remarks>
public sealed class Restores(FabDbContext context, ILogger<Restores> logger)
{
    public async Task<RestoreAct> ApplyAsync(
        string text,
        RestoreRoots? answered,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);

        // ADR 0010's window, asked of the installation rather than written into
        // the endpoint, because the other unauthenticated write asks the same
        // question and there must not be two notions of it.
        if (installation.PasswordHash is not null)
        {
            return Refused(RestoreOutcome.NotOffered);
        }

        var intake = BackupReader.Read(text);

        if (intake.Outcome is BackupIntakeOutcome.NotABackup)
        {
            return Refused(RestoreOutcome.NotABackup);
        }

        if (intake.Outcome is BackupIntakeOutcome.FromANewerTool)
        {
            return Refused(RestoreOutcome.FromANewerTool) with { WrittenBy = intake.ToolVersion };
        }

        var document = intake.Document!;

        if (await WhatIsAlreadyHereAsync(cancellationToken) is { Count: > 0 } found)
        {
            return Refused(RestoreOutcome.NotEmpty) with { Found = found };
        }

        var summary = Describe(document);

        if (answered is null)
        {
            return new RestoreAct(
                RestoreOutcome.RootsNeeded,
                Restore.Sentence(RestoreOutcome.RootsNeeded),
                summary);
        }

        var library = Trimmed(answered.Library);
        var downloads = Trimmed(answered.Downloads);

        if (RefuseRoot(library, downloads, summary.NeedsLibraryRoot, BackupRoot.Library) is { } libraryRefusal)
        {
            return Refused(RestoreOutcome.LibraryRootRefused) with
            {
                Summary = summary,
                Refusal = libraryRefusal,
                RefusedRoot = BackupRoot.Library,
                Detail = Restore.RootSentence(libraryRefusal, BackupRoot.Library),
            };
        }

        if (RefuseRoot(downloads, library, summary.NeedsDownloadDirectory, BackupRoot.Downloads) is { } downloadsRefusal)
        {
            return Refused(RestoreOutcome.DownloadDirectoryRefused) with
            {
                Summary = summary,
                Refusal = downloadsRefusal,
                RefusedRoot = BackupRoot.Downloads,
                Detail = Restore.RootSentence(downloadsRefusal, BackupRoot.Downloads),
            };
        }

        var roots = new BackupRoots(library, downloads);

        // The re-rooting refusal, and it runs over the whole document before
        // a row is written: a segment climbing out of its root would put a
        // restored path somewhere the person did not point at, and there is no
        // second chance to notice once the rows are in.
        if (Unplaceable(document, roots) is { Count: > 0 } stray)
        {
            return Refused(RestoreOutcome.PathOutsideItsRoot) with
            {
                Summary = summary,
                Found = stray,
            };
        }

        await WriteAsync(installation, document, roots, cancellationToken);

        logger.LogInformation(
            "Restored a Backup written by {ToolVersion} at {WrittenAt}: {Indexers} indexer(s), "
            + "{Rules} automation rule(s), {Entries} library entry/entries, {Downloads} download(s), "
            + "{Arriving} arriving file(s).",
            document.ToolVersion,
            document.WrittenAt,
            summary.Indexers,
            summary.AutomationRules,
            summary.LibraryEntries,
            summary.Downloads,
            summary.ArrivingFiles);

        return new RestoreAct(
            RestoreOutcome.Restored,
            Restore.Sentence(RestoreOutcome.Restored),
            summary);
    }

    private static RestoreAct Refused(RestoreOutcome outcome) =>
        new(outcome, Restore.Sentence(outcome));

    private static string? Trimmed(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    /// <summary>
    /// What makes this installation non-empty, as ADR 0009 names it: an
    /// Indexer, an Automation Rule or a Library Entry.
    /// </summary>
    /// <remarks>
    /// Those three and not a fourth. They are the three a person recognises as
    /// <em>their</em> installation having been used, which is what the refusal
    /// is protecting; the Catalogue and the caches fill themselves on a fresh
    /// container within minutes and would make the window close on its own.
    /// </remarks>
    private async Task<List<string>> WhatIsAlreadyHereAsync(CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var indexers = await context.Indexers.CountAsync(cancellationToken);
        var rules = await context.AutomationRules.CountAsync(cancellationToken);
        var entries = await context.LibraryEntries.CountAsync(cancellationToken);

        if (indexers > 0) found.Add($"{indexers} indexer(s) are configured.");
        if (rules > 0) found.Add($"{rules} automation rule(s) are configured.");
        if (entries > 0) found.Add($"{entries} library entry/entries are held.");

        return found;
    }

    private static BackupSummary Describe(BackupDocument document)
    {
        var used = Restore.RootsUsed(document);

        return new BackupSummary(
            document.FormatVersion,
            document.ToolVersion,
            document.WrittenAt,
            // The Installation's own Library root counts, so that an
            // installation which finished onboarding and filed nothing yet is
            // still asked where its library is.
            used.Contains(BackupRoot.Library) || document.Installation.LibraryRoot is { Length: > 0 },
            used.Contains(BackupRoot.Downloads) || document.Installation.PathMappingTo is { Length: > 0 },
            document.Installation.LibraryRoot,
            document.Installation.PathMappingTo,
            document.Indexers.Count,
            document.AutomationRules.Count,
            document.LibraryEntries.Count,
            document.VideoFiles.Count,
            document.Downloads.Count,
            document.ArrivingFiles.Count);
    }

    /// <summary>
    /// Whether a root the document needs can be used, asked by opening it.
    /// </summary>
    /// <param name="other">
    /// The other root, so that ADR 0010's overlap rule is checked here too — a
    /// Restore is the one moment both are answered at once, which makes it the
    /// only place the pair can be wrong together.
    /// </param>
    private static RootRefusal? RefuseRoot(string? path, string? other, bool needed, BackupRoot root)
    {
        if (path is null)
        {
            return needed ? RootRefusal.Unanswered : null;
        }

        if (!Path.IsPathRooted(path)) return RootRefusal.NotAbsolute;

        if (!Directories.Exists(path)) return RootRefusal.Missing;

        // The Library is written into; the Download Directory is only read, and
        // ADR 0009 is explicit that a Restore neither deletes content nor
        // invents a Filing. Asking for more than is used would refuse a
        // read-only downloads mount, which is a reasonable way to run this.
        if (root is BackupRoot.Library && !Directories.IsWritable(path)) return RootRefusal.NotWritable;

        if (root is BackupRoot.Downloads && !Directories.IsReadable(path)) return RootRefusal.NotReadable;

        if (other is null) return null;

        return LibraryRoot.Compare(path, other) switch
        {
            PathOverlap.Same => RootRefusal.TheSameAsTheOther,
            PathOverlap.Inside or PathOverlap.Contains => RootRefusal.OverlapsTheOther,
            _ => null,
        };
    }

    /// <summary>Every path the answered roots cannot place, as sentences.</summary>
    private static List<string> Unplaceable(BackupDocument document, BackupRoots roots) =>
        [.. Restore.Paths(document)
            .Where(found => roots.Absolute(found.Path) is null)
            .Select(found => $"{found.Where}: {found.Path.Root}/{found.Path.Path}")
            .Take(10)];

    private async Task WriteAsync(
        InstallationRow installation,
        BackupDocument document,
        BackupRoots roots,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var carried = document.Installation;

        installation.PasswordHash = carried.PasswordHash;
        installation.PrdbApiKey = carried.PrdbApiKey;
        installation.PrdbUserHash = carried.PrdbUserHash;

        // The two answered roots replace what the file recorded. Everything
        // else about the mapping is SABnzbd's own view of its filesystem and is
        // no more ours to re-root than its category is.
        installation.LibraryRoot = carried.LibraryRoot is { Length: > 0 } ? roots.Library : null;
        installation.SabnzbdUrl = carried.SabnzbdUrl;
        installation.SabnzbdApiKey = carried.SabnzbdApiKey;
        installation.SabnzbdCategory = carried.SabnzbdCategory;
        installation.PathMappingFrom = carried.PathMappingFrom;
        installation.PathMappingTo = carried.PathMappingTo is { Length: > 0 } ? roots.Downloads : null;

        installation.OnboardingStep = carried.OnboardingStep;
        installation.SabnzbdSkipped = carried.SabnzbdSkipped;
        installation.IndexersSkipped = carried.IndexersSkipped;
        installation.PlanShortSince = carried.PlanShortSince;
        installation.RetryBudget = carried.RetryBudget;
        installation.PreferredDownloadQuality = carried.PreferredDownloadQuality;
        installation.AutomaticDownloadCap = carried.AutomaticDownloadCap;
        installation.DeleteLeftovers = carried.DeleteLeftovers;
        installation.ReportFulfilments = carried.ReportFulfilments;
        installation.ReportConfirmedAssignments = carried.ReportConfirmedAssignments;
        installation.WhatsNewObservedAt = carried.WhatsNewObservedAt;

        // ADR 0033's translation, and there is nothing to translate back into:
        // the Catalogue is cache and this container has none yet. What's New
        // then counts everything as new, which is true of a fresh installation
        // and is what the export decided this field would carry.
        installation.WhatsNewObservedVideoId = null;

        context.Installation.Update(installation);

        // The five seeded rows are this build's opinion about ADR 0006's gates,
        // and the document carries the person's. Replaced rather than merged:
        // the table is the setting, so a union of the two would be a third
        // answer nobody chose.
        await context.GateAdmissions.ExecuteDeleteAsync(cancellationToken);

        context.GateAdmissions.AddRange(document.GateAdmissions.Select(row => new GateAdmissionRow
        {
            Gate = row.Gate,
            Confidence = row.Confidence,
        }));

        context.Indexers.AddRange(document.Indexers.Select(row => new IndexerRow
        {
            Id = row.Id,
            Name = row.Name,
            Url = row.Url,
            ApiKey = row.ApiKey,
            Categories = row.Categories,
            LastVerdict = row.LastVerdict,
            LastCheckedAt = row.LastCheckedAt,
            Enabled = row.Enabled,
            Rank = row.Rank,
            DailyQueryBudget = row.DailyQueryBudget,
        }));

        context.AutomationRules.AddRange(document.AutomationRules.Select(row => new AutomationRuleRow
        {
            Id = row.Id,
            Name = row.Name,
            Enabled = row.Enabled,
            MinimumSize = row.MinimumSize,
            MaximumSize = row.MaximumSize,
        }));

        context.AutomationRuleIndexers.AddRange(
            document.AutomationRuleIndexers.Select(row => new AutomationRuleIndexerRow
            {
                AutomationRuleId = row.AutomationRuleId,
                IndexerId = row.IndexerId,
            }));

        context.LibraryEntries.AddRange(document.LibraryEntries.Select(row => new LibraryEntryRow
        {
            VideoId = row.VideoId,
            EntryDirectory = roots.Absolute(row.EntryDirectory)!,
            FiledAt = row.FiledAt,
        }));

        context.VideoFiles.AddRange(document.VideoFiles.Select(row => new VideoFileRow
        {
            Id = row.Id,
            LibraryEntryVideoId = row.LibraryEntryVideoId,
            FiledPath = roots.Absolute(row.FiledPath)!,
            QualityLabel = row.QualityLabel,
            SizeBytes = row.SizeBytes,
            RuntimeSeconds = row.RuntimeSeconds,
            Width = row.Width,
            Height = row.Height,
            VideoCodec = row.VideoCodec,
            OsHash = row.OsHash,
        }));

        context.Downloads.AddRange(document.Downloads.Select(row => new DownloadRow
        {
            Id = row.Id,
            VideoId = row.VideoId,
            IndexerId = row.IndexerId,
            DerivedReleaseId = row.DerivedReleaseId,
            SubmittedName = row.SubmittedName,
            NzoId = row.NzoId,
            SubmissionState = row.SubmissionState,
            State = row.State,
            Cause = row.Cause,
            LastSabnzbdStatus = row.LastSabnzbdStatus,
            FailMessage = row.FailMessage,
            StageLog = row.StageLog,
            Storage = row.Storage,
            ConsecutiveAbsences = row.ConsecutiveAbsences,
            OutstandingSince = row.OutstandingSince,
            TidiedAt = row.TidiedAt,
            OriginIsPerson = row.OriginIsPerson,
            CreatedAt = row.CreatedAt,
        }));

        context.DownloadOriginRules.AddRange(
            document.DownloadOriginRules.Select(row => new DownloadOriginRuleRow
            {
                Id = row.Id,
                DownloadId = row.DownloadId,
                AutomationRuleId = row.AutomationRuleId,
                RuleName = row.RuleName,
            }));

        context.ArrivingFiles.AddRange(document.ArrivingFiles.Select(row => new ArrivingFileRow
        {
            Id = row.Id,
            DownloadId = row.DownloadId,
            IndexerId = row.IndexerId,
            DerivedReleaseId = row.DerivedReleaseId,
            SourcePath = roots.Absolute(row.SourcePath)!,
            ArrivedName = row.ArrivedName,
            IsOnDisk = row.IsOnDisk,
            State = row.State,
            Reason = row.Reason,
            VideoId = row.VideoId,
            SiteId = row.SiteId,
            SizeBytes = row.SizeBytes,
            RuntimeSeconds = row.RuntimeSeconds,
            Width = row.Width,
            Height = row.Height,
            VideoCodec = row.VideoCodec,
            QualityLabel = row.QualityLabel,
            OsHash = row.OsHash,
            IntendedPath = row.IntendedPath is null ? null : roots.Absolute(row.IntendedPath),
            LastAttemptedAt = row.LastAttemptedAt,
            Confidence = row.Confidence,
            MatchedBy = row.MatchedBy,
            ProbeOutcome = row.ProbeOutcome,
            ProbeError = row.ProbeError,
        }));

        context.ArrivingFileCandidates.AddRange(
            document.ArrivingFileCandidates.Select(row => new ArrivingFileCandidateRow
            {
                ArrivingFileId = row.ArrivingFileId,
                VideoId = row.VideoId,
            }));

        context.ReportedStates.AddRange(document.ReportedStates.Select(row => new ReportedStateRow
        {
            VideoId = row.VideoId,
            UserHash = row.UserHash,
            IsFulfilled = row.IsFulfilled,
            Quality = row.Quality,
            FulfilledAt = row.FulfilledAt,
            TerminalOutcome = row.TerminalOutcome,
        }));

        context.ConfirmedAssignments.AddRange(
            document.ConfirmedAssignments.Select(row => new ConfirmedAssignmentRow
            {
                OsHash = row.OsHash,
                VideoId = row.VideoId,
                UserHash = row.UserHash,
                SizeBytes = row.SizeBytes,
                ArrivalFileName = row.ArrivalFileName,
                ReleaseName = row.ReleaseName,
                RuntimeSeconds = row.RuntimeSeconds,
                Width = row.Width,
                Height = row.Height,
                VideoCodec = row.VideoCodec,
                PrdbAnswer = row.PrdbAnswer,
                SentAt = row.SentAt,
            }));

        context.OperationLogEntries.AddRange(
            document.OperationLog.Select(row => new OperationLogEntryRow
            {
                Id = row.Id,
                Act = row.Act,
                VideoFileId = row.VideoFileId,
                LibraryEntryVideoId = row.LibraryEntryVideoId,
                VideoId = row.VideoId,
                DownloadId = row.DownloadId,
                PathBefore = row.PathBefore is null ? null : roots.Absolute(row.PathBefore),
                PathAfter = row.PathAfter is null ? null : roots.Absolute(row.PathAfter),
                DisplacedPath = row.DisplacedPath is null ? null : roots.Absolute(row.DisplacedPath),
                LeftoverNamesJson = row.LeftoverNamesJson,
                Actor = row.Actor,
                Reason = row.Reason,
                At = row.At,
            }));

        context.AccountPreferenceWrites.AddRange(
            document.AccountPreferenceWrites.Select(row => new AccountPreferenceWriteRow
            {
                Id = row.Id,
                Kind = row.Kind,
                EntityId = row.EntityId,
                Desired = row.Desired,
                RequestedAt = row.RequestedAt,
                LastFailure = row.LastFailure,
                Blocked = row.Blocked,
            }));

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
