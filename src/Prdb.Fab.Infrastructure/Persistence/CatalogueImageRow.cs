namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueImage</c>: one image prdb publishes for a video, and
/// what the artwork cache knows about it.
/// </summary>
/// <remarks>
/// The row is the record of the image; the bytes are a file under the data
/// directory named by <see cref="PrdbId"/>, which is ADR 0030's reason for
/// naming by image id rather than by video id — a changed choice is a different
/// filename, so nothing has to decide whether the bytes it finds are current.
/// </remarks>
public sealed class CatalogueImageRow
{
    public long Id { get; set; }

    /// <summary>prdb's image id, and the name the cached file carries.</summary>
    public Guid PrdbId { get; set; }

    public long VideoId { get; set; }

    public CatalogueVideoRow? Video { get; set; }

    /// <summary>
    /// The absolute URL prdb hands out in its own payload. ADR 0030 fetches it
    /// straight rather than through the SDK, which is why nothing here passes
    /// the governor.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Where this image stood in the <c>images[]</c> prdb last published for the
    /// video, counted from zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0027 chooses the first entry carrying a non-null URL, on the ground
    /// that prdb documents the array as ordered oldest first with the image id
    /// breaking ties — a guaranteed order, expressly not a ranking. The choice
    /// is reproducible only if the order survives being written down, and
    /// nothing else on the row carries it: the payload has no stamp to sort by,
    /// and the surrogate key says when the tool first saw the image rather than
    /// when prdb published it.
    /// </para>
    /// <para>
    /// So the position is quoted from the payload. A detail read rewrites it for
    /// every image of the video at once, because that payload is the authority
    /// on the whole array; the images feed puts a newly arrived image last,
    /// which is where a feed paged by creation time says it belongs.
    /// </para>
    /// </remarks>
    public int Position { get; set; }

    /// <summary>Whether the bytes are in the cache.</summary>
    public bool Cached { get; set; }

    /// <summary>
    /// Whether the URL was found dead. ADR 0030: marked once and never retried
    /// on a schedule, because prdb hard-deletes image rows, so a 404 is
    /// normally permanent. A transport failure is not this and leaves no mark.
    /// </summary>
    public bool FoundDead { get; set; }

    /// <summary>
    /// When a grid was last served this image. ADR 0030 evicts the unpinned
    /// part least-recently-served first, and this is that order. Null until
    /// something has asked for it.
    /// </summary>
    public DateTimeOffset? LastServedAt { get; set; }
}
