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

    /// <summary>The profile image selected by prdb, never exposed to the browser.</summary>
    public string? ProfileImageUrl { get; set; }

    /// <summary>A cache-file identity in the shared artwork namespace.</summary>
    public Guid? ArtworkCacheKey { get; set; }

    public bool ArtworkCached { get; set; }

    public bool ArtworkFoundDead { get; set; }

    public DateTimeOffset? ArtworkLastServedAt { get; set; }

    public DateTimeOffset? ProfileCheckedAt { get; set; }
}
