namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueVideoActor</c>: who is credited on a video. ADR 0013
/// has a catalogue row reference actors rather than copy them, since the actors
/// feed already holds them whole.
/// </summary>
public sealed class CatalogueVideoActorRow
{
    public long VideoId { get; set; }

    public CatalogueVideoRow? Video { get; set; }

    public long ActorId { get; set; }

    public CatalogueActorRow? Actor { get; set; }
}
