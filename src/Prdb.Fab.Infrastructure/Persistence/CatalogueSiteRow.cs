namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueSite</c>: the producer a video was released by.
/// </summary>
/// <remarks>
/// The change feed marks explicit tombstones without discarding historical
/// identity. Its stamp prevents stale snapshots reviving a deleted Site.
/// </remarks>
public sealed class CatalogueSiteRow
{
    public long Id { get; set; }

    /// <summary>prdb's own id, and what a library entry names.</summary>
    public Guid PrdbId { get; set; }

    public required string Title { get; set; }

    /// <summary>
    /// The network prdb groups the site under, where there is one. A column and
    /// not a table of its own: nothing joins on it, and <c>CONTEXT.md</c>
    /// reserves the word against <strong>Site</strong> for everything else.
    /// </summary>
    public string? Network { get; set; }

    /// <summary>
    /// False once the site has stopped appearing in prdb's list. The row stays;
    /// this is what says it is history.
    /// </summary>
    public bool StillOffered { get; set; } = true;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string? Url { get; set; }
    public Guid? PlatformId { get; set; }
    public string? PlatformKey { get; set; }
    public string? PlatformTitle { get; set; }
    public string? AccountHandle { get; set; }
}
