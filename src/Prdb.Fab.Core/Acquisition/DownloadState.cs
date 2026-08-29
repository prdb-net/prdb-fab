namespace Prdb.Fab.Core.Acquisition;

/// <summary>The places a submitted Download can stand (ADRs 0016 and 0045).</summary>
public enum DownloadState
{
    Outstanding,
    Completed,
    Collected,
    Failed,
    Abandoned,
}

/// <summary>
/// Where a terminal failure was observed. It never interprets SABnzbd's
/// translated words.
/// </summary>
public enum DownloadCause
{
    Rejected,
    Failed,
    Unusable,
    Vanished,
    Abandoned,
    Empty,
}
