namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueSite</c>: the producer a video was released by.
/// </summary>
/// <remarks>
/// The one catalogue table with a deletion rule of its own: ADR 0013 never
/// deletes a site row, only marks it as no longer offered, because ADR 0005
/// builds a filed path out of the site title and a library entry must still be
/// able to name the site its path came from. The whole list arrives in one
/// request under an ETag, so this table is replaced rather than paged.
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
}
