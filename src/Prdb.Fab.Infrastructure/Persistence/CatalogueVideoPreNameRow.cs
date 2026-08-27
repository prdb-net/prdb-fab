namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>CatalogueVideoPreName</c>: one scene release title prdb
/// records for a video. A video may have several or none, and the same title is
/// what an indexer names a release after — which is the whole reason the
/// catalogue keeps them.
/// </summary>
public sealed class CatalogueVideoPreNameRow
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public CatalogueVideoRow? Video { get; set; }

    public required string PreName { get; set; }

    /// <summary>
    /// ADR 0025's stored comparison form, beside the pre-name for the same
    /// reason the video's title has one, and unindexed for the same reason.
    /// </summary>
    public string NormalisedPreName { get; set; } = string.Empty;

    /// <summary>
    /// ADR 0032 turned ADR 0025's resumable position into this flag: a needle
    /// added while a pass was running would sit behind a position and never be
    /// searched. False is <em>not yet searched</em>.
    /// </summary>
    public bool SearchedBackwards { get; set; }
}
