namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueActor</c>: a person credited on a video, as prdb's
/// change feed delivers them.
/// </summary>
public sealed class CatalogueActorRow
{
    public long Id { get; set; }

    public Guid PrdbId { get; set; }

    public required string Name { get; set; }
}
