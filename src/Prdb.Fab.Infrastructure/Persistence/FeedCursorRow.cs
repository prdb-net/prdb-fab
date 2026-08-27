using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>FeedCursor</c>: how far the sync has come with one of the
/// things it follows.
/// </summary>
/// <remarks>
/// <para>
/// One row per <see cref="Core.Catalogue.Feed"/>, and the feed itself is the
/// key — a second row for one feed would be two positions over one stream, with
/// nothing to say which of them is behind.
/// </para>
/// <para>
/// This is the one table in the schema whose account class is decided per row
/// rather than for the table: three of prdb's feeds are the user's own, and the
/// rest belong to no account. <see cref="Feeds.AccountClassOf"/> is where that
/// is written down.
/// </para>
/// </remarks>
public sealed class FeedCursorRow
{
    public Feed Feed { get; set; }

    /// <summary>
    /// What the next request resumes from, in whatever form the feed itself
    /// hands back: a cursor for the five change feeds, a timestamp for the
    /// high-water mark over the newest videos, a page for the one reading
    /// backwards. Text because a cursor is prdb's token to give and this tool's
    /// to hand back unread. Null before the first pass.
    /// </summary>
    public string? Cursor { get; set; }
}
