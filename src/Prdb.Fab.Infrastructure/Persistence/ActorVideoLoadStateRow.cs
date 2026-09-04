namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>The durable position of one person-requested Actor Catalogue Fill.</summary>
public sealed class ActorVideoLoadStateRow
{
    public long ActorId { get; set; }
    public CatalogueActorRow? Actor { get; set; }
    public int ResumePage { get; set; } = 1;
    public int VideosSeen { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// One locally held Video from an Actor Catalogue Fill. The rows are the
/// bounded reference that keeps exactly the current fill from catalogue
/// eviction.
/// </summary>
public sealed class ActorVideoLoadVideoRow
{
    public long ActorId { get; set; }
    public ActorVideoLoadStateRow? Load { get; set; }
    public long VideoId { get; set; }
    public CatalogueVideoRow? Video { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
}
