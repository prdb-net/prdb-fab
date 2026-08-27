using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The <c>FeedCursor</c> table, read and written as the positions it holds.
/// </summary>
/// <remarks>
/// One row per feed, so every write here is an upsert against the feed itself
/// (ADR 0033). The column is text because what it holds differs per row — a
/// position for the change feeds and the high-water mark, a page for the pass
/// reading backwards, a validator for the site list — and each of those is
/// somebody else's token or this tool's own writing rather than a value the
/// schema has an opinion about.
/// </remarks>
public sealed class FeedCursors(FabDbContext context)
{
    /// <summary>Where <paramref name="feed"/> has come to, or null before it has run.</summary>
    public async Task<FeedPosition?> PositionAsync(Feed feed, CancellationToken cancellationToken) =>
        FeedPosition.Read(await TokenAsync(feed, cancellationToken));

    /// <summary>
    /// Whether <paramref name="feed"/> has a row at all.
    /// </summary>
    /// <remarks>
    /// The question a one-shot bootstrap answers itself with: a feed with no
    /// row has never run, and a feed with one has, whatever it says. See
    /// <see cref="Core.Scheduling.IOneShot"/>.
    /// </remarks>
    public Task<bool> StartedAsync(Feed feed, CancellationToken cancellationToken) =>
        context.FeedCursors.AnyAsync(row => row.Feed == feed, cancellationToken);

    /// <summary>The stored text, whatever the feed makes of it.</summary>
    public async Task<string?> TokenAsync(Feed feed, CancellationToken cancellationToken) =>
        await context.FeedCursors
            .Where(row => row.Feed == feed)
            .Select(row => row.Cursor)
            .SingleOrDefaultAsync(cancellationToken);

    public Task SaveAsync(Feed feed, FeedPosition position, CancellationToken cancellationToken) =>
        SaveAsync(feed, position.Stored, cancellationToken);

    /// <summary>
    /// Writes the row, creating it where the feed has never run.
    /// </summary>
    /// <remarks>
    /// Not <c>ExecuteUpdate</c>: the row usually does not exist yet the first
    /// time each feed writes one, and an update that matches nothing is the
    /// quiet version of a feed that never advances.
    /// </remarks>
    public async Task SaveAsync(Feed feed, string? token, CancellationToken cancellationToken)
    {
        var row = await context.FeedCursors
            .AsTracking()
            .SingleOrDefaultAsync(stored => stored.Feed == feed, cancellationToken);

        if (row is null)
        {
            context.FeedCursors.Add(new FeedCursorRow { Feed = feed, Cursor = token });
        }
        else
        {
            row.Cursor = token;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
