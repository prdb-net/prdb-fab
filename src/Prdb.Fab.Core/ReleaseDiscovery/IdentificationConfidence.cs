namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The named confidence values prdb returns from Identification.</summary>
/// <remarks>
/// The declaration order quotes prdb's wire values and is never an ordering.
/// In particular, <see cref="Ambiguous"/> is numerically above
/// <see cref="Exact"/> while meaning that prdb declined to name one video.
/// </remarks>
public enum IdentificationConfidence
{
    None,
    Partial,
    Probable,
    Strong,
    Exact,
    Ambiguous,
}

/// <summary>The rung of prdb's Identification ladder that answered.</summary>
/// <remarks>
/// The first five quote prdb's wire values and their order. The last does not:
/// ADR 0062 admits one source of an Identification that is not prdb's ladder,
/// and it needs a name in the same column so that a person reading a Video File
/// can see where its Video came from.
/// </remarks>
public enum IdentificationRung
{
    OsHash,
    PHash,
    Filename,
    ReleaseName,
    Site,

    /// <summary>
    /// ADR 0062: an exact match against the osHash prdb publishes on a linked,
    /// currently visible user preview. <strong>Not a rung of prdb's ladder.</strong>
    /// </summary>
    /// <remarks>
    /// Numbered far outside prdb's range on purpose. The values above are
    /// prdb's own and the API has already added one since this enumeration was
    /// written (<c>Md5</c>, at 5); a local value taking the next free number
    /// would collide with the next one it adds, and the collision would show up
    /// as a Video File claiming prdb said something it did not.
    /// </remarks>
    PreviewHash = 100,
}
