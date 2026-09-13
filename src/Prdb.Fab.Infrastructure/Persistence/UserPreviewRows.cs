namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0061's <c>user_preview</c>: one picture a prdb user submitted, as much
/// of prdb's row as this tool reads, plus what the asset cache knows about it.
/// </summary>
/// <remarks>
/// <para>
/// Beside <see cref="CatalogueImageRow"/> and not inside it. The two are alike
/// enough to be tempting to merge and differ in every property that matters: one
/// file per row against a pair, an immutable object against one that can be
/// withdrawn and restored, a permanent <c>FoundDead</c> against a reversible
/// visibility, and a year-long cacheable response against one that has to be
/// revocable. Sharing a table would mean a nullable column for each of those.
/// </para>
/// <para>
/// Disposable, and not exported: metadata that can be fetched again, about
/// pictures that can be fetched again (ADR 0009).
/// </para>
/// </remarks>
public sealed class UserPreviewRow
{
    public long Id { get; set; }

    /// <summary>prdb's own id, and the name both cached files carry.</summary>
    public Guid PrdbId { get; set; }

    /// <summary>
    /// The Video prdb has linked this preview to, or null where nobody has.
    /// </summary>
    /// <remarks>
    /// prdb's id rather than the local surrogate, because a preview may name a
    /// Video this Catalogue does not hold — the change feed is global, and a
    /// linked row for a Video nothing here has ever read is ordinary. Kept as a
    /// value rather than as a foreign key for the same reason.
    /// </remarks>
    public Guid? VideoPrdbId { get; set; }

    /// <summary>
    /// The osHash of the file this preview was made from, in the one spelling
    /// this tool compares by (<c>UserPreviewHash</c>).
    /// </summary>
    public required string OsHash { get; set; }

    /// <summary>prdb's <c>previewImageType</c>: <c>Single</c> or <c>SpriteSheet</c>.</summary>
    /// <remarks>
    /// Quoted rather than parsed into an enumeration. The pattern on the upload
    /// form fixes the two words and their case-insensitivity, and nothing else
    /// in the document promises the list will not grow — a type this build does
    /// not know is a preview it does not show, which is a decision the reader
    /// makes rather than one a failed parse makes for it.
    /// </remarks>
    public required string Kind { get; set; }

    /// <summary>The absolute URL of the image object, as prdb published it.</summary>
    public required string Url { get; set; }

    /// <summary>The absolute URL of the paired WebVTT, where there is one.</summary>
    public string? VttUrl { get; set; }

    public long Filesize { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Where prdb puts this preview among the Video's, counted from zero.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>How many tiles the sprite sheet is cut into, or null for a single image.</summary>
    public int? TileCount { get; set; }

    public int? TileWidth { get; set; }

    public int? TileHeight { get; set; }

    public int? Columns { get; set; }

    public int? Rows { get; set; }

    /// <summary>prdb's <c>moderationStatus</c>, quoted for a person reading a log.</summary>
    public string? ModerationStatus { get; set; }

    /// <summary>prdb's <c>moderationVisibility</c>, quoted for the same reason.</summary>
    public string? ModerationVisibility { get; set; }

    /// <summary>
    /// The moderation signature this row carried the last time an endpoint that
    /// returns only publicly visible rows delivered it, or null where none ever
    /// has.
    /// </summary>
    /// <remarks>
    /// prdb documents no vocabulary for its two moderation strings, so this is
    /// what a change is measured against: the feed reporting a different
    /// signature is a withdrawal, and reporting this one again is a restoration.
    /// See <c>UserPreviewModeration</c>, where the whole argument is.
    /// </remarks>
    public string? ShownUnder { get; set; }

    /// <summary>Whether this preview may be served, shown or written out.</summary>
    /// <remarks>
    /// Stored rather than derived, which is the one place this schema departs
    /// from ADR 0033's preference — and it is not the derivable half that makes
    /// it necessary. A filtered list that <em>omits</em> a row it used to
    /// contain is a withdrawal with no payload at all, so there is nothing for a
    /// clause to read; and every gallery query filters on it, which wants an
    /// index. Both writers see a complete answer before they write it.
    /// </remarks>
    public bool Shown { get; set; }

    /// <summary>prdb's soft delete, which overrides everything else.</summary>
    public bool Deleted { get; set; }

    /// <summary>
    /// prdb's <c>updatedAtUtc</c> for this row, and the rule that keeps the
    /// snapshot and the feed from undoing each other: a payload older than this
    /// is discarded rather than applied.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Which version of this preview's pair is in the asset cache, or null
    /// where none is.
    /// </summary>
    /// <remarks>
    /// A version rather than a flag, because the pair is mutable: prdb may
    /// republish a sprite at a different URL or a different grid, and a
    /// half-replaced pair would be a sheet cut by somebody else's WebVTT. The
    /// value is only ever written once both halves are on disk, which is what
    /// makes it the commit.
    /// </remarks>
    public string? CachedVersion { get; set; }

    /// <summary>
    /// When this preview was last served to a browser. The eviction order, the
    /// same as ADR 0030's and for the same reason.
    /// </summary>
    public DateTimeOffset? LastServedAt { get; set; }
}

/// <summary>
/// ADR 0061's <c>user_preview_interest</c>: the Videos whose user previews this
/// installation has a reason to hold.
/// </summary>
/// <remarks>
/// <para>
/// Two acts write a row here and nothing else does — a person opening a Preview,
/// and a Video File being filed. It is the register that makes the change feed
/// due at all, the freshness that stops an empty answer being asked for twice in
/// a week, and the thing a cleanup pass expires.
/// </para>
/// <para>
/// Disposable, and not exported. A Restore brings the Library, the Library is
/// the interest, and the enrichment pass finds it as a work set to fill.
/// </para>
/// </remarks>
public sealed class UserPreviewInterestRow
{
    /// <summary>prdb's Video id, which is the key: one interest per Video.</summary>
    public Guid VideoPrdbId { get; set; }

    /// <summary>
    /// When prdb was last asked for this Video's user previews, whatever the
    /// answer was — including <em>none</em>, which is the ordinary answer and
    /// the one worth remembering.
    /// </summary>
    public DateTimeOffset? LastReadAt { get; set; }

    /// <summary>
    /// When something last had a reason to want them. What the expiry pass
    /// reads.
    /// </summary>
    public DateTimeOffset TouchedAt { get; set; }

    /// <summary>
    /// Whether a filed Video File is among the reasons, which outlives a glance
    /// at a sheet.
    /// </summary>
    /// <remarks>
    /// The Library's interest is not expired by time the way a browse is: a file
    /// on disk goes on being a file on disk, and its Timeline Preview has to be
    /// reconcilable for as long as it is there.
    /// </remarks>
    public bool ForTheLibrary { get; set; }
}
