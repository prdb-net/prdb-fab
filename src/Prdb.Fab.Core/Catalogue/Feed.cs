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
}
