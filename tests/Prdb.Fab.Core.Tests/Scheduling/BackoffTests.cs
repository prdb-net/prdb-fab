using Prdb.Fab.Core.Scheduling;

using Xunit;

namespace Prdb.Fab.Core.Tests.Scheduling;

public sealed class BackoffTests
{
    /// <summary>ADR 0014: the routine's own interval, doubled per failure.</summary>
    [Fact]
    public void A_failure_doubles_the_interval()
    {
        var cadence = TimeSpan.FromMinutes(1);

        Assert.Equal(cadence, Backoff.After(cadence, consecutiveFailures: 0));
        Assert.Equal(TimeSpan.FromMinutes(2), Backoff.After(cadence, 1));
        Assert.Equal(TimeSpan.FromMinutes(4), Backoff.After(cadence, 2));
        Assert.Equal(TimeSpan.FromMinutes(8), Backoff.After(cadence, 3));
    }

    /// <summary>
    /// Capped at an hour, and the cap is what keeps a routine that has been
    /// failing all week from having backed off into next year — which would be
    /// a routine that has stopped rather than one that is slow.
    /// </summary>
    [Fact]
    public void It_stops_doubling_at_an_hour()
    {
        Assert.Equal(Backoff.Longest, Backoff.After(TimeSpan.FromMinutes(15), consecutiveFailures: 4));
        Assert.Equal(Backoff.Longest, Backoff.After(TimeSpan.FromMinutes(15), 40));
        Assert.Equal(Backoff.Longest, Backoff.After(TimeSpan.FromHours(6), 1));
    }

    /// <summary>
    /// The doubling is done on a multiplier rather than on the ticks, so a long
    /// cadence and a long outage cannot overflow their way round to a routine
    /// that is due immediately — which is the one failure of this shape that
    /// would look like the tool working.
    /// </summary>
    [Fact]
    public void It_never_comes_back_round_to_soon()
    {
        for (var failures = 1; failures < 200; failures++)
        {
            Assert.Equal(Backoff.Longest, Backoff.After(TimeSpan.FromHours(2), failures));
        }
    }
}
