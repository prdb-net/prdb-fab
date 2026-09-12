namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// How much disk the artwork cache is allowed to take, as the byte figure
/// ADR 0030 chose over a row count.
/// </summary>
/// <remarks>
/// <para>
/// Bytes rather than rows, which is where this departs from
/// <see cref="CatalogueCeiling"/> and from the indexer cache. Those two bound
/// tables whose rows are all much the same size, so a count says what the disk
/// will be; here the thing being bounded <em>is</em> disk and images vary by an
/// order of magnitude, so a count would be the unpredictable number. Both
/// choices pass the same test — a figure that can be written in the
/// documentation and held in the head.
/// </para>
/// <para>
/// Eight gigabytes, and the figure is measured rather than round. A catalogue
/// of ~47 000 Videos publishes ~69 000 images averaging ~135 KB, which is
/// ~9.3 GB — so eight holds very nearly all of one. The two gigabytes this
/// replaced held ~15 000 images, and ADR 0059 measured what that cost on a
/// catalogue of that size: the cache stayed 1.2 % full, every browse grid went
/// to the CDN for most of its two dozen tiles, and the click took seconds
/// rather than the milliseconds the queries behind it take.
/// </para>
/// <para>
/// It bounds the <strong>unpinned</strong> half only. Pinned images are the
/// library grid and the file filing copies from, so they are neither counted
/// against this nor evicted to hold it — which makes the pinned half
/// proportional to what the user actually has, the same shape ADR 0013 gave the
/// catalogue.
/// </para>
/// <para>
/// Not a setting. ADR 0020 admits a control where the answer lives outside
/// anything the tool can observe, and the only thing this one would change is
/// how often a browse grid re-fetches a thumbnail.
/// </para>
/// </remarks>
public static class ArtworkCeiling
{
    /// <summary>The most disk the unpinned half of the cache may hold.</summary>
    public const long Bytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// How far a proactive fill takes the unpinned half, which is short of
    /// <see cref="Bytes"/> on purpose.
    /// </summary>
    /// <remarks>
    /// Six of the eight, so that the two above belong to whoever is actually
    /// browsing and a warm pass is never itself the cause of an eviction.
    /// Without the gap the two would fight: the pass would fill to the ceiling,
    /// the sweep would drop the least recently served — which is precisely a
    /// file warmed and not yet looked at — and the next pass would fetch it
    /// again. Two gigabytes of headroom is ~15 000 images, which is more than
    /// anybody serves between two passes of a routine that runs every thirty
    /// seconds.
    /// </remarks>
    public const long WarmTo = 6L * 1024 * 1024 * 1024;

    /// <summary>
    /// The most one image may weigh before it is refused. ADR 0030 puts a
    /// per-file ceiling in the governor's place, beside the timeout and the
    /// content check, because nothing upstream promises how large a CDN's
    /// answer is.
    /// </summary>
    /// <remarks>
    /// Sixteen megabytes, which is two orders of magnitude above what prdb's
    /// images actually run to. It is a stop rather than a budget: what it
    /// refuses is a redirect into something that is not an image at all, not a
    /// poster somebody published at an unusual size.
    /// </remarks>
    public const long AnImage = 16L * 1024 * 1024;

    /// <summary>
    /// How many bytes have to go for an unpinned half of <paramref name="held"/>
    /// to be back under <paramref name="ceiling"/>, and zero where none do.
    /// </summary>
    public static long OverBy(long held, long ceiling = Bytes) => held > ceiling ? held - ceiling : 0;
}
