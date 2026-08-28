namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The local cost filter that decides whether a Release is worth asking prdb about.</summary>
public static class Screening
{
    /// <summary>
    /// Whether any normalised catalogue title or Pre-Name occurs in the
    /// normalised Release title. A hit is a reason to ask, never an
    /// Identification.
    /// </summary>
    public static bool Hits(string releaseTitle, IReadOnlyCollection<string> needles) =>
        needles.Any(needle => needle.Length > 0 && releaseTitle.Contains(needle, StringComparison.Ordinal));
}
