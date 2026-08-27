using Prdb.Fab.Core.Catalogue;

using Xunit;

namespace Prdb.Fab.Core.Tests.Catalogue;

/// <summary>
/// ADR 0013's overlap, and the one thing that keeps it from being a trap.
/// </summary>
public sealed class FeedPositionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The failure this exists to prevent: prdb's <c>since</c> is a lower bound
    /// over a timestamp several rows can carry, so a position advanced to
    /// exactly the last value seen loses every row sharing it — which is
    /// precisely what a bulk import produces.
    /// </summary>
    [Fact]
    public void A_settled_position_is_asked_from_before_itself()
    {
        var position = FeedPosition.CaughtUpAt(Noon);

        Assert.Equal(Noon - FeedPosition.Overlap, position.Since);
        Assert.Null(position.SinceId);
    }

    /// <summary>
    /// And the other half. A page that ended with more behind it resumes from
    /// the exact row it stopped at, because a feed handing back a full page
    /// inside the overlap would otherwise be replayed from the same place for
    /// ever — the tool would ask, apply, and end up where it started.
    /// </summary>
    [Fact]
    public void A_walk_that_is_still_running_resumes_from_exactly_where_it_stopped()
    {
        var row = Guid.Parse("0f5c1a2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b");

        var position = FeedPosition.MidWalkAt(Noon, row);

        Assert.Equal(Noon, position.Since);
        Assert.Equal(row, position.SinceId);
    }

    /// <summary>
    /// The tie-breaker is never sent with a timestamp that has been moved. An
    /// id tie-breaks rows at one timestamp, so pairing it with an earlier one
    /// would skip everything in the overlap sorting below it — the overlap
    /// undoing itself.
    /// </summary>
    [Fact]
    public void A_moved_timestamp_never_carries_a_tie_breaker()
    {
        foreach (var position in (FeedPosition[])
                 [FeedPosition.CaughtUpAt(Noon), FeedPosition.MidWalkAt(Noon, Guid.NewGuid())])
        {
            Assert.True(position.Since == position.At || position.SinceId is null);
        }
    }

    [Fact]
    public void A_position_survives_being_written_down_and_read_back()
    {
        var row = Guid.NewGuid();

        foreach (var written in (FeedPosition[])
                 [FeedPosition.CaughtUpAt(Noon), FeedPosition.MidWalkAt(Noon, row)])
        {
            var read = FeedPosition.Read(written.Stored);

            Assert.Equal(written, read);
        }
    }

    /// <summary>
    /// A cursor is prdb's token in one direction and this tool's own writing in
    /// the other, and neither is worth failing a routine over: reading it back
    /// as nothing starts the feed again, which costs requests, where refusing to
    /// run costs the feed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("page 7")]
    public void Nothing_readable_is_no_position(string? stored) =>
        Assert.Null(FeedPosition.Read(stored));

    /// <summary>
    /// Text that names a time and then something that is not an id is still a
    /// time. The alternative — reading the whole thing as nothing — would drop a
    /// position over the half of it that is only an optimisation.
    /// </summary>
    [Fact]
    public void A_time_with_an_unreadable_tie_breaker_is_still_a_time() =>
        Assert.Equal(
            FeedPosition.CaughtUpAt(Noon),
            FeedPosition.Read($"{FeedPosition.CaughtUpAt(Noon).Stored}|not-an-id"));
}
