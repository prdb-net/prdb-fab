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
/// The first seven quote prdb's wire values and their order. The last two do
/// not: ADR 0062 admits one source of an Identification that is not prdb's
/// ladder, and a rung prdb adds after this enumeration was written still needs
/// a name in the same column, so that a person reading a Release or a Video
/// File can see where its Video came from.
/// </remarks>
public enum IdentificationRung
{
    OsHash,
    PHash,
    Filename,
    ReleaseName,
    Site,
    Md5,

    /// <summary>Site, release date and exact title of a scene-group Release.</summary>
    SiteDateTitle,

    /// <summary>
    /// ADR 0062: an exact match against the osHash prdb publishes on a linked,
    /// currently visible user preview. <strong>Not a rung of prdb's ladder.</strong>
    /// </summary>
    /// <remarks>
    /// Numbered far outside prdb's range on purpose. The values above are
    /// prdb's own and the API keeps adding to them; a local value taking the
    /// next free number would collide with the next one it adds, and the
    /// collision would show up as a Video File claiming prdb said something it
    /// did not.
    /// </remarks>
    PreviewHash = 100,

    /// <summary>
    /// A rung prdb returned that this enumeration does not know yet.
    /// <strong>Not a rung of prdb's ladder.</strong>
    /// </summary>
    /// <remarks>
    /// The named Video and its Confidence are kept so the answer reads
    /// correctly, but ADR 0006's gates never act on it: what an unknown rung
    /// proves is not known here, so an allowed Confidence alone is not enough.
    /// Numbered outside prdb's range for the same reason as
    /// <see cref="PreviewHash"/>.
    /// </remarks>
    Unrecognised = 101,
}
