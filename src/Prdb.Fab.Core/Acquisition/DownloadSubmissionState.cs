namespace Prdb.Fab.Core.Acquisition;

/// <summary>
/// Where the one permitted SABnzbd write stands independently of the Download
/// state that following observes afterwards.
/// </summary>
public enum DownloadSubmissionState
{
    /// <summary>The addfile request completed with a definitive answer.</summary>
    Submitted,

    /// <summary>The durable manual request exists but no addfile attempt has started.</summary>
    Pending,

    /// <summary>The attempt was durably marked before the addfile request began.</summary>
    Submitting,

    /// <summary>The request may have reached SABnzbd, so it must not be repeated.</summary>
    Unknown,
}
