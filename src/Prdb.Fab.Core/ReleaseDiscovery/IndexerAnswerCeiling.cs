namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>
/// The most one indexer answer may weigh before it is refused unread.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>ArtworkCeiling.AnImage</c>, and it is here for the same
/// reason: the indexer walk runs unattended against a service the user reached
/// by pasting a URL, and nothing upstream promises how large its answer is.
/// Buffering whatever arrives is how a misbehaving remote takes the container
/// down rather than one routine's run.
/// </para>
/// <para>
/// Both figures are stops rather than budgets. A page of a hundred releases is
/// tens of kilobytes of XML and an NZB for one release a few hundred; these sit
/// two orders of magnitude above that, so what they refuse is an answer that was
/// never the thing being asked for.
/// </para>
/// </remarks>
public static class IndexerAnswerCeiling
{
    /// <summary>The most one search or caps response may weigh.</summary>
    public const long ADocument = 32L * 1024 * 1024;

    /// <summary>The most one NZB may weigh.</summary>
    public const long AnNzb = 32L * 1024 * 1024;
}
