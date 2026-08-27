using Prdb.Fab.Core.Scheduling;

using Xunit;

namespace Prdb.Fab.Core.Tests.Scheduling;

public sealed class RestartSpreadTests
{
    /// <summary>
    /// ADR 0014's reason for existing: a container that was down comes back
    /// with everything overdue, and firing all of it at prdb and at every
    /// indexer in the same second is the shape a rate limit is least forgiving
    /// of.
    /// </summary>
    [Fact]
    public void Ten_overdue_routines_do_not_fire_in_the_same_second()
    {
        var cadence = TimeSpan.FromMinutes(15);

        var seconds = Enumerable.Range(0, 10)
            .Select(position => (int)RestartSpread.OffsetFor(position, count: 10, cadence).TotalSeconds)
            .ToArray();

        Assert.Equal(seconds.Distinct(), seconds);
    }

    /// <summary>
    /// Across the smaller of their own interval and five minutes, so a routine
    /// that runs every ten seconds is not held back for five minutes to spread
    /// it.
    /// </summary>
    [Fact]
    public void The_window_is_the_smaller_of_the_interval_and_five_minutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(2.5),
            RestartSpread.OffsetFor(position: 1, count: 2, TimeSpan.FromMinutes(15)));

        Assert.Equal(
            TimeSpan.FromSeconds(5),
            RestartSpread.OffsetFor(position: 1, count: 2, TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// The first is due at once, so a restart is never slower than no spread at
    /// all for whatever comes first — and one routine is already spread.
    /// </summary>
    [Fact]
    public void The_first_one_waits_for_nothing()
    {
        Assert.Equal(TimeSpan.Zero, RestartSpread.OffsetFor(0, count: 10, TimeSpan.FromMinutes(15)));
        Assert.Equal(TimeSpan.Zero, RestartSpread.OffsetFor(0, count: 1, TimeSpan.FromMinutes(15)));
    }

    /// <summary>
    /// ADR 0014: the live lane is exempt. A download in flight has to be picked
    /// up at once, and nothing in that lane leaves the container.
    /// </summary>
    [Fact]
    public void The_live_lane_is_exempt_and_the_other_three_are_not()
    {
        Assert.True(RestartSpread.Exempts(Lane.Live));

        Assert.False(RestartSpread.Exempts(Lane.Sync));
        Assert.False(RestartSpread.Exempts(Lane.Bulk));
        Assert.False(RestartSpread.Exempts(Lane.File));
    }
}
