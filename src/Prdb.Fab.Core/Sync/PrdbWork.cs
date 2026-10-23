namespace Prdb.Fab.Core.Sync;

/// <summary>
/// The purpose of a governed request, used for diagnostics and background pacing.
/// All unattended work protects the account reserve; repair and cached Release
/// Identification additionally preserve recurring demand and share a paced pool.
/// </summary>
public enum PrdbWork
{
    /// <summary>
    /// Checking a key somebody has just typed. Not one of ADR 0014's eight, and
    /// above all of them, because it is the only prdb request a person is
    /// sitting in front of and the only way back from a key that no longer
    /// works. It spends one request and cannot be deferred without making a
    /// spent budget unfixable from the inside.
    /// </summary>
    Verification,

    /// <summary>Unmetered, bounded refresh of the shared account counters.</summary>
    BudgetReading,

    /// <summary>
    /// <c>POST /videos/identify</c> for an arrived file. The file
    /// is waiting on it, and everything downstream of filing waits with it.
    /// </summary>
    Identification,

    /// <summary>
    /// Fulfilment reports and hash submissions. Rare, and ADR 0013 already
    /// queues them rather than dropping them.
    /// </summary>
    Writes,

    /// <summary>
    /// ADR 0060: the detail read a Preview asks for when the Catalogue holds no
    /// picture of the Video somebody has just opened.
    /// </summary>
    Preview,

    /// <summary>The wanted list and the two favourites feeds.</summary>
    UserFeeds,

    /// <summary>The newest videos, and the pass reading backwards from them.</summary>
    WhatsNew,

    /// <summary>Bounded Catalogue correction through Video changes.</summary>
    CatalogueChanges,

    /// <summary>The video images feed.</summary>
    Images,

    /// <summary>The actors feed.</summary>
    Actors,

    /// <summary>The Site change feed and its legacy baseline safety net.</summary>
    Sites,

    /// <summary>
    /// ADR 0061: the user previews read for a filed Video File, and the change
    /// feed that follows what moderation does to them.
    /// </summary>
    UserPreviews,

    /// <summary>
    /// ADR 0064: a generated Sprite Sheet and its paired WebVTT, submitted to
    /// prdb for a Video File this installation filed.
    /// </summary>
    Publications,

    /// <summary>
    /// ADR 0013's repair pass, sharing the governed background pool.
    /// </summary>
    Repair,

    /// <summary>Background cached Release names, below essential account and file work.</summary>
    ReleaseIdentification,
}
