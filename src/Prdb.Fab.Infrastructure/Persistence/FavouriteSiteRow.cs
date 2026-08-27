namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>FavouriteSite</c>: a site the user follows in prdb.
/// Account-scoped, and dropped with the rest of the user's half when the key
/// turns out to be somebody else's.
/// </summary>
public sealed class FavouriteSiteRow
{
    public long SiteId { get; set; }

    public CatalogueSiteRow? Site { get; set; }

    public DateTimeOffset SinceAt { get; set; }
}
