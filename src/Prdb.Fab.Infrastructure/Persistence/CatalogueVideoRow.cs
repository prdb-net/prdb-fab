namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueVideo</c>: one video as prdb described it when the
/// tool last looked. A cache row rather than a mirrored one — it exists because
/// something looked at it, it stays while something local points at it, and
/// ADR 0013 drops the rest.
/// </summary>
public sealed class CatalogueVideoRow
{
    /// <summary>
    /// An integer surrogate. ADR 0033 spends a UUIDv7 only where a row crosses
    /// the export boundary, and nothing in the catalogue does: every row of it
    /// refetches itself by running.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// prdb's own id, and the natural key everything outside the cache names
    /// this video by — a library entry, a download, a reported state. ADR 0033
    /// makes that the reason the export boundary is closed.
    /// </summary>
    public Guid PrdbId { get; set; }

    public required string Title { get; set; }

    /// <summary>
    /// ADR 0023's comparison form of <see cref="Title"/>, stored so that the
    /// same function writes the needle and the haystack and the two cannot
    /// drift apart (ADR 0025).
    /// </summary>
    /// <remarks>
    /// Written by the upsert that brings the row in, which is not built yet.
    /// Deliberately not indexed: ADR 0025 measured a trigram index costing more
    /// disk than the table it indexes and still losing to one indexless pass
    /// per batch.
    /// </remarks>
    public string NormalisedTitle { get; set; } = string.Empty;

    /// <summary>
    /// The site prdb released it under, once that row has arrived. Null while
    /// it has not: ADR 0013 replaces the site list wholesale from a request of
    /// its own, so a video read before that pass is a video whose site is not
    /// known here yet rather than a video that is wrong.
    /// </summary>
    public long? SiteId { get; set; }

    public CatalogueSiteRow? Site { get; set; }

    /// <summary>Nullable, as prdb's own document has it.</summary>
    public DateOnly? ReleaseDate { get; set; }

    /// <summary>
    /// The Consensus Runtime, in prdb's spelling. ADR 0033 keeps the column
    /// names as quotations of the API field, because renaming a quotation is
    /// how a reader stops being able to find it in the OpenAPI document — the
    /// <em>term</em> for what these three hold is Consensus Runtime, and
    /// ADR 0031 is explicit that none of them decides anything.
    /// </summary>
    public long? DurationMs { get; set; }

    public long? DurationSpreadMs { get; set; }

    public int? DurationFileCount { get; set; }

    /// <summary>prdb's stamp, not this tool's. What a correction moves.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// When this row was last read back from prdb. ADR 0013's repair pass takes
    /// pinned videos oldest-checked-first, and this is the order it takes them
    /// in.
    /// </summary>
    public DateTimeOffset LastReadAt { get; set; }

    /// <summary>
    /// Whether the backwards search has been past this title (ADR 0032). False
    /// means <em>not yet searched</em>, which is what a row written today has
    /// to say — the reader arrives with the indexer cache, and a row that sat
    /// unsearched with no error and no Gap is ADR 0015's silently skipped row
    /// one layer up.
    /// </summary>
    public bool TitleSearchedBackwards { get; set; }
}
