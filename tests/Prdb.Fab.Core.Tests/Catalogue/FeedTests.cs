using Prdb.Fab.Core.Catalogue;

using Xunit;

namespace Prdb.Fab.Core.Tests.Catalogue;

public sealed class FeedTests
{
    /// <summary>
    /// ADR 0013: a key belonging to a different prdb account drops the user
    /// half of the local data — the wanted list, the favourites, and those
    /// three cursors — and keeps the catalogue, which belongs to no account.
    /// Named one by one, because the failure this guards against is a cursor
    /// surviving into another account's installation and resuming a walk over
    /// answers it can no longer see.
    /// </summary>
    [Fact]
    public void The_users_three_feeds_are_the_ones_a_key_change_drops()
    {
        var scoped = Feeds.All.Where(feed => feed.AccountClassOf() == AccountClass.AccountScoped);

        Assert.Equal(
            [Feed.WantedVideos, Feed.FavouriteSites, Feed.FavouriteActors],
            scoped);
    }

    /// <summary>
    /// And every other feed says so rather than falling through. The switch
    /// throws over a feed nobody classified, which is what makes adding one an
    /// answered question instead of an inherited default.
    /// </summary>
    [Fact]
    public void Every_feed_says_whether_it_belongs_to_the_account()
    {
        foreach (var feed in Feeds.All)
        {
            Assert.NotEqual(AccountClass.PerRow, feed.AccountClassOf());
            Assert.NotEqual(AccountClass.AccountStamped, feed.AccountClassOf());
        }
    }
}
