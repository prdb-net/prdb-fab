namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>FavouriteActor</c>: an actor the user follows in prdb.
/// Account-scoped, for the same reason as <see cref="FavouriteSiteRow"/>.
/// </summary>
public sealed class FavouriteActorRow
{
    public long ActorId { get; set; }

    public CatalogueActorRow? Actor { get; set; }

    public DateTimeOffset SinceAt { get; set; }
}
