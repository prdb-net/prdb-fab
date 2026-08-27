namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// What the schema cannot say about <c>FeedCursor</c>, because its rows do not
/// share one answer.
/// </summary>
public static class Feeds
{
    public static IReadOnlyList<Feed> All { get; } = Enum.GetValues<Feed>();

    /// <summary>
    /// Whether this row goes when the prdb key turns out to belong to somebody
    /// else. Three of them do: a cursor into another account's wanted list or
    /// favourites would resume a walk over answers this installation can no
    /// longer see (ADR 0013).
    /// </summary>
    /// <remarks>
    /// Written as a switch over the whole enumeration rather than as a set of
    /// the three, so that a feed added later does not quietly inherit
    /// <see cref="AccountClass.AccountFree"/> — which is the failure that shows
    /// up as one account's list surviving into another's installation.
    /// </remarks>
    public static AccountClass AccountClassOf(this Feed feed) => feed switch
    {
        Feed.WantedVideos or Feed.FavouriteSites or Feed.FavouriteActors => AccountClass.AccountScoped,
        Feed.Actors or Feed.VideoImages or Feed.WhatsNew or Feed.WhatsNewBackfill or Feed.Sites =>
            AccountClass.AccountFree,
        _ => throw new ArgumentOutOfRangeException(
            nameof(feed),
            feed,
            "A feed has to say whether it belongs to the prdb account (ADR 0033)."),
    };
}
