namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>WantedVideo</c>: a video the user has marked in prdb as one
/// they want to have. Usually projected from prdb's feed; ADR 0048 also writes
/// the desired state before a manual acquisition leaves the tool.
/// </summary>
/// <remarks>
/// Account-scoped: a key belonging to a different prdb account drops this table
/// (ADR 0013). The catalogue video it names stays, because it belongs to no
/// account.
/// </remarks>
public sealed class WantedVideoRow
{
    /// <summary>
    /// The catalogue video, and the key. One row per video is what the list is,
    /// and it is also one of the columns ADR 0033's pinning anti-join reads —
    /// which a primary key already indexes.
    /// </summary>
    public long VideoId { get; set; }

    public CatalogueVideoRow? Video { get; set; }

    /// <summary>Since when prdb says it has been wanted.</summary>
    public DateTimeOffset SinceAt { get; set; }
}
