namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>What has happened to one Indexer's part of a Manual Search.</summary>
public enum ManualSearchIndexerState
{
    Queued,
    Searching,
    Deferred,
    Searched,
    Failed,
}

/// <summary>The phase a person sees for one complete Manual Search.</summary>
public enum ManualSearchPhase
{
    Queued,
    Searching,
    Deferred,
    Identifying,
    Complete,
    Failed,
}

public enum ManualSearchStartOutcome
{
    Started,
    AlreadyRunning,
    VideoNotFound,
    TitleNotSearchable,
    NoEnabledIndexers,
    IndexerNotEnabled,
}

public enum ManualSearchRetryOutcome
{
    Scheduled,
    SearchNotFound,
    IndexerNotSelected,
    NotRetryable,
}
