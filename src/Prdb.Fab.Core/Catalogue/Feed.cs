namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// One thing the sync follows, and one row of <c>FeedCursor</c>.
/// </summary>
/// <remarks>
/// ADR 0013's five change feeds, and the two positions What's New needs beside
/// them. <c>CONTEXT.md</c> reserves <em>Cursor</em> against <strong>Watermark</strong>,
/// which is the indexer walk's word; ADR 0033 keeps prdb's word here because
/// prdb's API is what names and documents these.
/// </remarks>
public enum Feed
{
    /// <summary>prdb's actor change feed.</summary>
    Actors,

    /// <summary>
    /// prdb's video image change feed. It is documented as never emitting a
    /// deletion, which is half of why ADR 0013 needs a repair pass at all.
    /// </summary>
    VideoImages,

    /// <summary>The user's wanted list.</summary>
    WantedVideos,

    /// <summary>The sites the user follows.</summary>
    FavouriteSites,

    /// <summary>The actors the user follows.</summary>
    FavouriteActors,

    /// <summary>
    /// How far the newest videos have been read. Not a change feed: prdb has
    /// none for videos, so this is the high-water mark over <c>createdAtUtc</c>
    /// that ADR 0013 sets back by an overlap window on every pass.
    /// </summary>
    WhatsNew,

    /// <summary>
    /// How far the first pass has come the other way, into what was published
    /// before the tool was installed. Bounded by a page count and retired when
    /// it reaches the end, which is why it is a position of its own rather than
    /// a second meaning for <see cref="WhatsNew"/>.
    /// </summary>
    WhatsNewBackfill,

    /// <summary>
    /// Which version of prdb's site list is held: the <c>ETag</c> the last
    /// answer carried, handed back as <c>If-None-Match</c>.
    /// </summary>
    /// <remarks>
    /// ADR 0013 says sites have no feed, no cursor and no diff, and all three
    /// are true — the whole list fits one request, so there is nothing to resume
    /// from and nothing to page. What is left is still a token prdb gave and
    /// this tool hands back unread, which is what this table holds; a column on
    /// the installation row would be a sync position kept among the settings.
    /// </remarks>
    Sites,
}
