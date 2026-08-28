namespace Prdb.Fab.Core.Acquisition;

/// <summary>The four places a submitted Download can stand (ADR 0016).</summary>
public enum DownloadState
{
    Outstanding,
    Completed,
    Collected,
    Failed,
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
