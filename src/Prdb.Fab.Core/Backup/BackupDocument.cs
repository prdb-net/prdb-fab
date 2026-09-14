using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Core.Backup;

/// <summary>
/// The Backup: everything about one installation that cannot be fetched again,
/// and nothing that can be.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0009's envelope — the format version, the version of the tool that wrote
/// it, when it was written — and then one section per exported table. ADR 0033
/// runs the boundary between tables rather than through one, so a section is a
/// whole table and there is no section for a table that is not exported.
/// </para>
/// <para>
/// Which tables those are is not a list kept here by hand.
/// <c>BackupSections</c> pairs each section with the entity type it carries and
/// checks the pairing against the model before anything is written, so a table
/// that starts crossing the boundary cannot be left out silently.
/// </para>
/// <para>
/// Three things the sections do that the columns behind them do not. Paths are
/// root-relative (<see cref="BackupPath"/>), because the installation reading
/// this file may mount its library elsewhere. Every reference out of the
/// boundary is an identifier an outside authority owns — prdb's Video and Site
/// ids, an Indexer's derived Release identity — never a local surrogate, which
/// is what lets the caches stay out of the file without anything dangling.
/// And the secret fields travel as they are stored, which under ADR 0057 is
/// where they stay: nothing in this tool encrypts anything, and what carries
/// the file is what encrypts it.
/// </para>
/// </remarks>
/// <param name="FormatVersion">
/// <see cref="BackupFormat.Version"/> as of the writing. Deliberately not the
/// EF migration version.
/// </param>
/// <param name="ToolVersion">
/// The informational version of the build that wrote the file, which ADR 0009
/// needs to refuse a document from a newer tool by name.
/// </param>
/// <param name="WrittenAt">
/// When the document was written. ADR 0009 calls this the export time;
/// <c>CONTEXT.md</c> reserves that word against <strong>Backup</strong>, so the
/// field says what happened rather than which word the ADR reached for.
/// </param>
public sealed record BackupDocument(
    int FormatVersion,
    string ToolVersion,
    DateTimeOffset WrittenAt,
    BackupInstallation Installation,
    IReadOnlyList<BackupGateAdmission> GateAdmissions,
    IReadOnlyList<BackupIndexer> Indexers,
    IReadOnlyList<BackupAutomationRule> AutomationRules,
    IReadOnlyList<BackupAutomationRuleIndexer> AutomationRuleIndexers,
    IReadOnlyList<BackupLibraryEntry> LibraryEntries,
    IReadOnlyList<BackupVideoFile> VideoFiles,
    IReadOnlyList<BackupDownload> Downloads,
    IReadOnlyList<BackupDownloadOriginRule> DownloadOriginRules,
    IReadOnlyList<BackupArrivingFile> ArrivingFiles,
    IReadOnlyList<BackupArrivingFileCandidate> ArrivingFileCandidates,
    IReadOnlyList<BackupReportedState> ReportedStates,
    IReadOnlyList<BackupConfirmedAssignment> ConfirmedAssignments,
    IReadOnlyList<BackupOperationLogEntry> OperationLog,
    IReadOnlyList<BackupAccountPreferenceWrite> AccountPreferenceWrites,
    IReadOnlyList<BackupIdentificationFlag> IdentificationFlags,
    IReadOnlyList<BackupPreviewPublication> PreviewPublications);

/// <summary>
/// The one Installation row. Its key is not in the document: ADR 0033 leaves
/// this table the one constant key in the schema, because a table that holds
/// exactly one row has no identity to restore.
/// </summary>
/// <param name="LibraryRoot">
/// Absolute, and one of the two roots every other path in the document is
/// relative to. Carried so Restore can prefill the question rather than answer
/// it (ADR 0009).
/// </param>
/// <param name="WhatsNewObservedVideo">
/// prdb's id for the newest Video a loaded page has shown, where the column
/// behind it holds the Catalogue's local surrogate. The translation is ADR
/// 0033's closure rule applied the way that ADR applies it to paths: the
/// database keeps what is cheapest to read, the document keeps what an outside
/// authority owns. A Restore whose Catalogue does not hold that Video yet has
/// nothing to point at, and then What's New counts everything as new — which is
/// true of a fresh installation.
/// </param>
public sealed record BackupInstallation(
    string? PasswordHash,
    string? PrdbApiKey,
    string? PrdbUserHash,
    string? LibraryRoot,
    string? SabnzbdUrl,
    string? SabnzbdApiKey,
    string? SabnzbdCategory,
    string? PathMappingFrom,
    string? PathMappingTo,
    OnboardingStep OnboardingStep,
    bool SabnzbdSkipped,
    bool IndexersSkipped,
    DateTimeOffset? PlanShortSince,
    int RetryBudget,
    PreferredDownloadQuality PreferredDownloadQuality,
    int AutomaticDownloadCap,
    bool DeleteLeftovers,
    bool ReportFulfilments,
    bool ReportConfirmedAssignments,
    bool PublishGeneratedPreviews,
    DateTimeOffset? PreviewPublicationExplainedAt,
    DateTimeOffset? WhatsNewObservedAt,
    Guid? WhatsNewObservedVideo);

/// <summary>ADR 0006's two admission sets, as the rows that are their form.</summary>
public sealed record BackupGateAdmission(string Gate, IdentificationConfidence Confidence);

/// <summary>
/// ADR 0062's flag: a filed Video File whose Video was named from evidence prdb
/// has since withdrawn or corrected.
/// </summary>
/// <remarks>
/// In the document because it cannot be fetched again — the preview rows it is
/// about are gone by definition, so a Restore arriving without it would show the
/// file as ordinarily identified and the question would never be asked a second
/// time.
/// </remarks>
/// <summary>
/// ADR 0064's publication: a generated Sprite Sheet this installation submitted
/// to prdb, or finished with without submitting.
/// </summary>
/// <remarks>
/// <para>
/// In the document for the reason ADR 0017 gave for recording a filed path, in
/// its strongest form: <em>which submission was accepted</em> cannot be fetched
/// again. prdb's three read endpoints answer with publicly visible rows only,
/// and a row in moderation is not one — so a Restore arriving without this
/// would find a Library of files none of which had ever been published, and
/// publish every one of them a second time. There is no retraction.
/// </para>
/// <para>
/// <strong>What is not here is the picture.</strong> The generated bytes are
/// disposable and remakeable from the file and the output version, and so is
/// everything that describes them — the grid, the weight, the attempt counter,
/// the moment the pair was committed. A row restored in
/// <see cref="Core.Sync.PreviewPublicationState.Ready"/> finds no bytes,
/// returns to its intent and is generated again, which is safe because a pair
/// waiting to be sent has never left the machine. A row restored in
/// <see cref="Core.Sync.PreviewPublicationState.Sending"/> is the case the
/// state exists for: a document written while a request was in flight, which
/// nothing can settle and nothing sends again.
/// </para>
/// </remarks>
/// <param name="UserHash">
/// The prdb account it went out under. Account-scoped as a Fulfilment is, and
/// for the stronger version of that reason: a queue sent under somebody else's
/// key puts a picture in a gallery that is not theirs.
/// </param>
/// <param name="PrdbImageId">
/// prdb's <c>videoUserImageId</c>, where a submission was accepted. The one
/// field here that names something outside this installation.
/// </param>
/// <param name="SubmittedUnder">
/// The moderation signature the submission entered under, quoted rather than
/// parsed (ADR 0061), so that a later change is recognisable as a change.
/// </param>
public sealed record BackupPreviewPublication(
    Guid Id,
    Guid VideoFileId,
    Guid VideoPrdbId,
    string OsHash,
    string UserHash,
    int OutputVersion,
    PreviewPublicationState State,
    string? Note,
    DateTimeOffset IntendedAt,
    DateTimeOffset? SettledAt,
    Guid? PrdbImageId,
    Guid? ModerationTargetId,
    string? SubmittedUnder);

public sealed record BackupIdentificationFlag(
    Guid VideoFileId,
    Guid VideoId,
    Guid? NowNamesVideoId,
    string Reason,
    DateTimeOffset At);

public sealed record BackupIndexer(
    Guid Id,
    string Name,
    string Url,
    string ApiKey,
    string Categories,
    IndexerConnectionOutcome LastVerdict,
    DateTimeOffset LastCheckedAt,
    bool Enabled,
    int Rank,
    int DailyQueryBudget);

public sealed record BackupAutomationRule(
    Guid Id,
    string Name,
    bool Enabled,
    long? MinimumSize,
    long? MaximumSize);

public sealed record BackupAutomationRuleIndexer(Guid AutomationRuleId, Guid IndexerId);

/// <param name="VideoId">
/// prdb's Video id, which ADR 0033 makes this row's whole identity.
/// </param>
public sealed record BackupLibraryEntry(
    Guid VideoId,
    BackupPath EntryDirectory,
    DateTimeOffset FiledAt);

public sealed record BackupVideoFile(
    Guid Id,
    Guid LibraryEntryVideoId,
    BackupPath FiledPath,
    string QualityLabel,
    long SizeBytes,
    long? RuntimeSeconds,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? OsHash);

/// <param name="DerivedReleaseId">
/// ADR 0015's derived Release identity, and the reason a consumed Release stays
/// consumed across a Restore: the Release row itself is cache and will be gone,
/// but the next walk derives the same identity from the same guid.
/// </param>
/// <param name="Storage">
/// Where SABnzbd said the job landed, in SABnzbd's own view of the filesystem.
/// Not one of this tool's roots — the path mapping is what turns it into one —
/// so it travels as the remote string it is.
/// </param>
public sealed record BackupDownload(
    Guid Id,
    Guid VideoId,
    Guid IndexerId,
    string DerivedReleaseId,
    string SubmittedName,
    string? NzoId,
    DownloadSubmissionState SubmissionState,
    DownloadState State,
    DownloadCause? Cause,
    string? LastSabnzbdStatus,
    string? FailMessage,
    string? StageLog,
    string? Storage,
    int ConsecutiveAbsences,
    DateTimeOffset OutstandingSince,
    DateTimeOffset? TidiedAt,
    bool OriginIsPerson,
    DateTimeOffset CreatedAt);

/// <param name="AutomationRuleId">
/// Null once the rule is gone, with <paramref name="RuleName"/> still saying
/// which it was — ADR 0046's complete automatic Origin, which is why deleting a
/// rule leaves this row readable rather than dangling.
/// </param>
public sealed record BackupDownloadOriginRule(
    Guid Id,
    Guid DownloadId,
    Guid? AutomationRuleId,
    string RuleName);

/// <param name="SourcePath">Under the Download Directory (ADR 0047).</param>
/// <param name="IntendedPath">Under the Library, and only while Filing.</param>
public sealed record BackupArrivingFile(
    Guid Id,
    Guid DownloadId,
    Guid IndexerId,
    string DerivedReleaseId,
    BackupPath SourcePath,
    string ArrivedName,
    bool IsOnDisk,
    ArrivingFileState State,
    ArrivingFileReason? Reason,
    Guid? VideoId,
    Guid? SiteId,
    long SizeBytes,
    long? RuntimeSeconds,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? QualityLabel,
    string? OsHash,
    BackupPath? IntendedPath,
    DateTimeOffset? LastAttemptedAt,
    IdentificationConfidence? Confidence,
    IdentificationRung? MatchedBy,
    ProbeOutcome ProbeOutcome,
    string? ProbeError);

public sealed record BackupArrivingFileCandidate(Guid ArrivingFileId, Guid VideoId);

public sealed record BackupReportedState(
    Guid VideoId,
    string UserHash,
    bool IsFulfilled,
    FulfilmentQuality? Quality,
    DateTimeOffset? FulfilledAt,
    ReportingOutcome? TerminalOutcome);

public sealed record BackupConfirmedAssignment(
    string OsHash,
    Guid VideoId,
    string UserHash,
    long SizeBytes,
    string ArrivalFileName,
    string ReleaseName,
    long? RuntimeSeconds,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? PrdbAnswer,
    DateTimeOffset? SentAt);

/// <param name="PathBefore">
/// Where the Video File was before the act — which for the first move of a
/// download is under the Download Directory and afterwards under the Library,
/// so the root travels with the path.
/// </param>
/// <param name="LeftoverNamesJson">
/// The JSON array the column holds, as the text it holds. Parsing it into a
/// list here would be one more thing that can fail while a person is writing a
/// Backup, and it would not survive a value this tool did not write; the
/// document is the record, so the record travels.
/// </param>
public sealed record BackupOperationLogEntry(
    Guid Id,
    string Act,
    Guid? VideoFileId,
    Guid? LibraryEntryVideoId,
    Guid? VideoId,
    Guid? DownloadId,
    BackupPath? PathBefore,
    BackupPath? PathAfter,
    BackupPath? DisplacedPath,
    string? LeftoverNamesJson,
    string Actor,
    string Reason,
    DateTimeOffset At);

/// <param name="EntityId">
/// prdb's id for whatever the preference is about, which
/// <paramref name="Kind"/> says.
/// </param>
public sealed record BackupAccountPreferenceWrite(
    Guid Id,
    AccountPreferenceKind Kind,
    Guid EntityId,
    bool Desired,
    DateTimeOffset RequestedAt,
    string? LastFailure,
    bool Blocked);
