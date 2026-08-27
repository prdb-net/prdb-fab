namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// The four lanes of ADR 0014. Each is one worker, not a share of a pool:
/// ADR 0038 rejected a semaphore over many routines because a semaphore
/// serialises but cannot take turns, and ADR 0032 requires turns.
/// </summary>
public enum Lane
{
    /// <summary>Obligations measured in seconds — following a download.</summary>
    Live,

    /// <summary>Talking to prdb and to the indexers.</summary>
    Sync,

    /// <summary>Work that may take a while and nothing waits on.</summary>
    Bulk,

    /// <summary>Moving files, alone, so that nothing else waits behind a copy.</summary>
    File,
}
