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

    public int? Gender { get; set; }
    public string? GenderLabel { get; set; }
    public DateOnly? Birthday { get; set; }
    public int? BirthdayType { get; set; }
    public string? BirthdayTypeLabel { get; set; }
    public DateOnly? Deathday { get; set; }
    public string? Birthplace { get; set; }
    public int? Haircolor { get; set; }
    public string? HaircolorLabel { get; set; }
    public int? Eyecolor { get; set; }
    public string? EyecolorLabel { get; set; }
    public int? BreastType { get; set; }
    public string? BreastTypeLabel { get; set; }
    public int? Height { get; set; }
    public int? BraSize { get; set; }
    public string? BraSizeLabel { get; set; }
    public int? WaistSize { get; set; }
    public int? HipSize { get; set; }
    public int? Nationality { get; set; }
    public string? NationalityLabel { get; set; }
    public int? Ethnicity { get; set; }
    public string? EthnicityLabel { get; set; }
    public int? CareerStart { get; set; }
    public int? CareerEnd { get; set; }
    public string? Tattoos { get; set; }
    public string? Piercings { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>The profile image selected by prdb, never exposed to the browser.</summary>
    public string? ProfileImageUrl { get; set; }

    /// <summary>A cache-file identity in the shared artwork namespace.</summary>
    public Guid? ArtworkCacheKey { get; set; }

    public bool ArtworkCached { get; set; }

    public bool ArtworkFoundDead { get; set; }

    public DateTimeOffset? ArtworkLastServedAt { get; set; }

    public DateTimeOffset? ProfileCheckedAt { get; set; }
}

public sealed class CatalogueActorAliasRow
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    public CatalogueActorRow? Actor { get; set; }
    public required string Name { get; set; }
    public Guid? SitePrdbId { get; set; }
}

public sealed class CatalogueActorBioRow
{
    public long Id { get; set; }
    public Guid PrdbId { get; set; }
    public long ActorId { get; set; }
    public CatalogueActorRow? Actor { get; set; }
    public required string Text { get; set; }
}

public sealed class CatalogueActorLinkRow
{
    public long Id { get; set; }
    public long ActorId { get; set; }
    public CatalogueActorRow? Actor { get; set; }
    public int? ExternalSite { get; set; }
    public required string ExternalSiteLabel { get; set; }
    public required string Url { get; set; }
}

public sealed class CatalogueActorImageRow
{
    public long Id { get; set; }
    public Guid PrdbId { get; set; }
    public long ActorId { get; set; }
    public CatalogueActorRow? Actor { get; set; }
    public int Position { get; set; }
    public int? ImageType { get; set; }
    public required string ImageTypeLabel { get; set; }
    public string? Url { get; set; }
}
