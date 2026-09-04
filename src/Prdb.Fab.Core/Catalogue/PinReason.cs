namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// Why a catalogue row is kept, and — since ADR 0033 made pinning a query
/// rather than a column — the name of the clause that matched.
/// </summary>
/// <remarks>
/// <para>
/// <c>CONTEXT.md</c>'s list of what may point at a catalogue video, written
/// whole rather than only as far as this slice reaches. Each source arrives
/// with the feature that owns it and contributes one <c>EXISTS</c> beside the
/// others rather than a rewrite.
/// </para>
/// <para>
/// Writing the list out now is the same move <see cref="Sync.PrdbWork"/> made
/// with ADR 0014's order of precedence: a name that is already here cannot be
/// invented differently by whoever builds its table, and the question
/// <em>what can pin a row</em> has one answer rather than one per feature.
/// </para>
/// <para>
/// Nothing stores any of these. A stored reason would have many writers and no
/// reader that would notice a mistake, which is the argument ADR 0033 used to
/// correct ADR 0013 — so this names a clause in a query and never a column.
/// </para>
/// </remarks>
public enum PinReason
{
    /// <summary>A library entry holds the video. ADR 0013 keeps these pinned even across a key change.</summary>
    LibraryEntry,

    /// <summary>The user has marked the video as wanted in prdb.</summary>
    WantedVideo,

    /// <summary>A download was submitted for the video.</summary>
    Download,

    /// <summary>A review queue entry asks a person which video a file is.</summary>
    ReviewQueueEntry,

    /// <summary>
    /// The video is one of the candidates of an open review queue entry.
    /// ADR 0013 names this separately so that eviction cannot empty a choice
    /// nobody has made yet.
    /// </summary>
    CandidateVideo,

    /// <summary>
    /// A cached release that was downloaded, consumed, or identified as a video
    /// still wanted names it.
    /// </summary>
    CachedRelease,

    /// <summary>A recent person-requested Manual Search names the video.</summary>
    ManualSearch,

    /// <summary>A person-requested Actor Catalogue Fill names the video.</summary>
    ActorCatalogueFill,
}
