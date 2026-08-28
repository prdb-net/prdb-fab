namespace Prdb.Fab.Core.Acquisition;

/// <summary>The local state transition caused by one successful SABnzbd observation.</summary>
public static class DownloadFollowing
{
    public const int AbsencesBeforeVanished = 3;

    public static DownloadFollowResult Found(DownloadSignal signal) => signal switch
    {
        DownloadSignal.Outstanding => new(DownloadState.Outstanding, null, 0),
        DownloadSignal.Completed => new(DownloadState.Completed, null, 0),
        DownloadSignal.Failed => new(DownloadState.Failed, DownloadCause.Failed, 0),
        DownloadSignal.Unusable => new(DownloadState.Failed, DownloadCause.Unusable, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(signal)),
    };

    public static DownloadFollowResult Absent(int previousConsecutiveAbsences)
    {
        var absences = previousConsecutiveAbsences + 1;
        return absences >= AbsencesBeforeVanished
            ? new(DownloadState.Failed, DownloadCause.Vanished, absences)
            : new(DownloadState.Outstanding, null, absences);
    }
}

public enum DownloadSignal
{
    Outstanding,
    Completed,
    Failed,
    Unusable,
}

public sealed record DownloadFollowResult(
    DownloadState State,
    DownloadCause? Cause,
    int ConsecutiveAbsences);
