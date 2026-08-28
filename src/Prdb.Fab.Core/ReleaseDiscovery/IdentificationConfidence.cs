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
public enum IdentificationRung
{
    OsHash,
    PHash,
    Filename,
    ReleaseName,
    Site,
}
