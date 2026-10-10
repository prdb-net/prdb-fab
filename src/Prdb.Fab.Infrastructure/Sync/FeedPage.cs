using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>A received page after successful idempotent application.</summary>
/// <remarks>Received counts remote items; Applied counts locally useful rows.</remarks>
public sealed record FeedPage(
    int Applied,
    bool HasMore,
    DateTimeOffset? CursorAt,
    Guid? CursorId,
    DateTimeOffset? ServerTimeUtc,
    int Received)
{
    public static FeedPage Nothing { get; } = new(0, false, null, null, null, 0);

    /// <summary>
    /// Nonempty pages retain both cursor fields even at the end. Empty pages
    /// ignore an echoed legacy cursor and use the already settled server bound.
    /// Invalid responses leave progress untouched; client time is never used.
    /// </summary>
    public FeedPosition? Next() => (Received, CursorAt, CursorId, ServerTimeUtc, HasMore) switch
    {
        (> 0, { } at, { } row, _, _) => FeedPosition.MidWalkAt(at, row),
        (0, _, _, { } bound, false) => FeedPosition.CaughtUpAt(bound),
        _ => null,
    };
}
