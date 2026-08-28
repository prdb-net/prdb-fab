using Prdb.Fab.Core.Acquisition;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>ADR 0033's exported record of one submission and its consumption.</summary>
public sealed class DownloadRow
{
    public Guid Id { get; set; }
    public Guid VideoId { get; set; }
    public Guid IndexerId { get; set; }
    public string DerivedReleaseId { get; set; } = string.Empty;
    public string SubmittedName { get; set; } = string.Empty;
    public string? NzoId { get; set; }
    public DownloadState State { get; set; }
    public DownloadCause? Cause { get; set; }
    public string? LastSabnzbdStatus { get; set; }
    public string? FailMessage { get; set; }
    public string? StageLog { get; set; }
    /// <summary>The completed history path as SABnzbd reported it.</summary>
    public string? Storage { get; set; }
    public int ConsecutiveAbsences { get; set; }
    public DateTimeOffset OutstandingSince { get; set; }
    public DateTimeOffset? TidiedAt { get; set; }
    public bool OriginIsPerson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One seven-day diagnostic fact produced by a real ranking decision.</summary>
public sealed class ReleaseNotDownloadedRow
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Reason { get; set; } = string.Empty;
}
