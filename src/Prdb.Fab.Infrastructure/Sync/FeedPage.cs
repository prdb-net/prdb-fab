using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// One page of a prdb change feed, as much of it as deciding the next position
/// needs.
/// </summary>
/// <remarks>
/// The seven feeds share one page envelope and this is it, with the rows
/// already applied: what varies between them is the shape of an item, and that
/// is the one thing a caller of a feed never has to know. ADR 0035 keeps the
/// generated payload types out of everything above this.
/// </remarks>
/// <param name="Applied">How many rows the page was worth. What the run log records.</param>
/// <param name="HasMore">Whether prdb says there is another page behind this one.</param>
/// <param name="CursorAt">
/// The <c>updatedAtUtc</c> of prdb's own next cursor, or null where it sent
/// none. Nullable in every response schema and documented for no condition, so
/// the fall-back is <paramref name="ServerTimeUtc"/> rather than a guess.
/// </param>
/// <param name="CursorId">The tie-breaker beside it.</param>
/// <param name="ServerTimeUtc">
/// prdb's clock when the page was produced, read before the rows were queried —
/// which is what makes it safe to keep as a lower bound. The API document says
/// so in as many words, and says as plainly that this tool's own clock is not a
/// substitute for it.
/// </param>
public sealed record FeedPage(
    int Applied,
    bool HasMore,
    DateTimeOffset? CursorAt,
    Guid? CursorId,
    DateTimeOffset? ServerTimeUtc)
{
    /// <summary>
    /// prdb answered with nothing at all. Not an error — the SDK returns null
    /// from a call it could not build a body from — and not a reason to move a
    /// position that was right before the request went out.
    /// </summary>
    public static FeedPage Nothing { get; } = new(0, HasMore: false, null, null, null);

    /// <summary>
    /// Where the feed stands after this page, or null where the page said
    /// nothing that could move it.
    /// </summary>
    public FeedPosition? Next() => (CursorAt, HasMore, CursorId, ServerTimeUtc) switch
    {
        // Still walking, and the walk knows the exact row it stopped at. No
        // overlap: see FeedPosition, where the reason is the whole argument.
        ({ } at, true, { } row, _) => FeedPosition.MidWalkAt(at, row),

        // Caught up, or as caught up as a page with no tie-breaker can say.
        ({ } at, _, _, _) => FeedPosition.CaughtUpAt(at),

        // No cursor came back. The server's clock is the documented fall-back
        // and is a lower bound the server itself will read back.
        (null, _, _, { } clock) => FeedPosition.CaughtUpAt(clock),

        _ => null,
    };
}
