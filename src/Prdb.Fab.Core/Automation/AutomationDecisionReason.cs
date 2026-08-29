namespace Prdb.Fab.Core.Automation;

/// <summary>One local explanation for a deliberate automatic non-act.</summary>
public enum AutomationDecisionReason
{
    NotWanted,
    ConfidenceGate,
    Size,
    IndexerNotAllowed,
    HeldVideo,
    OpenReviewQueue,
    AutomaticDownloadCap,
    RetryBudgetSpent,
    NoReleasesLeft,
    DownloadInFlight,
}
