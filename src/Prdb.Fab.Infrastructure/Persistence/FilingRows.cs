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
