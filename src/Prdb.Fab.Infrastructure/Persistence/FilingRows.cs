using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>One directory in the library, identified by the video it represents.</summary>
public sealed class LibraryEntryRow
{
    public Guid VideoId { get; set; }
    public string EntryDirectory { get; set; } = string.Empty;
    public DateTimeOffset FiledAt { get; set; }
}

/// <summary>One physical video file belonging to a library entry.</summary>
public sealed class VideoFileRow
{
    public Guid Id { get; set; }
    public Guid LibraryEntryVideoId { get; set; }
    public string FiledPath { get; set; } = string.Empty;
    public string QualityLabel { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long? RuntimeSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoCodec { get; set; }
    public string? OsHash { get; set; }
}

/// <summary>One supported video file discovered in a completed download.</summary>
public sealed class ArrivingFileRow
{
    public Guid Id { get; set; }
    public Guid DownloadId { get; set; }
    public Guid IndexerId { get; set; }
    public string DerivedReleaseId { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ArrivedName { get; set; } = string.Empty;
    public bool IsOnDisk { get; set; } = true;
    public ArrivingFileState State { get; set; }
    public ArrivingFileReason? Reason { get; set; }
    public Guid? VideoId { get; set; }
    public Guid? SiteId { get; set; }
    public long SizeBytes { get; set; }
    public long? RuntimeSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoCodec { get; set; }
    public string? QualityLabel { get; set; }
    public string? OsHash { get; set; }
    public string? IntendedPath { get; set; }
    public DateTimeOffset? LastAttemptedAt { get; set; }
    public IdentificationConfidence? Confidence { get; set; }
    public IdentificationRung? MatchedBy { get; set; }
    public ProbeOutcome ProbeOutcome { get; set; }
    public string? ProbeError { get; set; }
}

/// <summary>One candidate edge retained as identification evidence.</summary>
public sealed class ArrivingFileCandidateRow
{
    public Guid ArrivingFileId { get; set; }
    public Guid VideoId { get; set; }
}

/// <summary>A person's durable confirmation, scoped to the account that made it.</summary>
public sealed class ConfirmedAssignmentRow
{
    public string OsHash { get; set; } = string.Empty;
    public Guid VideoId { get; set; }
    public string UserHash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ArrivalFileName { get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
    public long? RuntimeSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoCodec { get; set; }
    public string? PrdbAnswer { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>One immutable record of a filesystem act.</summary>
public sealed class OperationLogEntryRow
{
    public Guid Id { get; set; }
    public string Act { get; set; } = string.Empty;
    public Guid? VideoFileId { get; set; }
    public Guid? LibraryEntryVideoId { get; set; }
    public Guid? VideoId { get; set; }
    public Guid? DownloadId { get; set; }
    public string? PathBefore { get; set; }
    public string? PathAfter { get; set; }
    public string? DisplacedPath { get; set; }
    public string? LeftoverNamesJson { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

/// <summary>Membership in one named identification gate.</summary>
public sealed class GateAdmissionRow
{
    public string Gate { get; set; } = string.Empty;
    public IdentificationConfidence Confidence { get; set; }
}

/// <summary>
/// What the background pass last found about one Video File.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own, and deliberately <em>not</em> exported. ADR 0033 runs
/// the boundary between tables, so a column on <c>video_file</c> would travel
/// in the Backup — and a restored installation would then claim its files had
/// been confirmed on a machine it is no longer running on. This is local,
/// derived, disposable knowledge in exactly the sense ADR 0009 means by a
/// cache: throw it away and the pass fills it in again.
/// </para>
/// <para>
/// That is also what makes the work set self-emptying. A Restore writes no
/// rows here, so every Video File it brought is unverified, the routine is due
/// while any remain, and it stops being due when none do.
/// </para>
/// </remarks>
public sealed class LibraryVerificationRow
{
    public Guid VideoFileId { get; set; }
    public LibraryVerification Outcome { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// ADR 0062's flag: a filed Video File whose Video was named from evidence prdb
/// has since withdrawn or corrected.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing on disk moves for a withdrawal.</strong> A filed file is
/// content the person now has, in a place their media server knows, and a
/// moderator taking a picture down is not grounds for this tool to rename,
/// delete or reassign it. So the answer is a record and a person, which is what
/// this is.
/// </para>
/// <para>
/// <strong>Exported</strong>, and it is the only thing in the user-preview slice
/// besides the Library's own assets that is. It cannot be re-derived: the
/// preview rows it is about are gone by definition, so a Restore that arrived
/// without it would show a filed file as ordinarily identified and the question
/// would never be asked again. That is ADR 0009's <em>cannot be fetched
/// again</em>, read literally.
/// </para>
/// <para>
/// One row per Video File, cleared by a person. A second withdrawal on a file
/// that is already flagged rewrites the reason rather than queueing behind it —
/// what a person needs is the current state, not a history of it.
/// </para>
/// </remarks>
public sealed class IdentificationFlagRow
{
    public Guid VideoFileId { get; set; }

    /// <summary>The Video the file is filed under, which is not changed by this.</summary>
    public Guid VideoId { get; set; }

    /// <summary>
    /// The Video the evidence names now, where it names one — a correction
    /// rather than a withdrawal. Null where the evidence simply went.
    /// </summary>
    public Guid? NowNamesVideoId { get; set; }

    /// <summary>Why it was flagged, as a sentence for the person deciding.</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
