using Microsoft.Extensions.Time.Testing;

using Prdb.Fab.Core.Access;

using Xunit;

namespace Prdb.Fab.Core.Tests.Access;

/// <summary>
/// ADR 0010: sign-in is rate-limited, because one password with no username is
/// the easiest thing in the world to try repeatedly.
/// </summary>
public sealed class SignInThrottleTests
{
    private static (SignInThrottle Throttle, FakeTimeProvider Time) Throttle()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

        return (new SignInThrottle(time), time);
    }

    [Fact]
    public void Nothing_is_held_back_before_anything_has_failed()
    {
        var (throttle, _) = Throttle();

        Assert.Null(throttle.RetryAfter());
    }

    [Fact]
    public void Guessing_stops_at_the_limit()
    {
        var (throttle, _) = Throttle();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerWindow; attempt++)
        {
            Assert.Null(throttle.RetryAfter());
            throttle.RecordFailure();
        }

        Assert.NotNull(throttle.RetryAfter());
    }

    [Fact]
    public void The_window_reopens_on_its_own()
    {
        var (throttle, time) = Throttle();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerWindow; attempt++)
        {
            throttle.RecordFailure();
        }

        time.Advance(SignInThrottle.Window);

        Assert.Null(throttle.RetryAfter());
    }

    /// <summary>
    /// The owner who knows their password waits once rather than repeatedly —
    /// which is what makes counting for the installation rather than per caller
    /// affordable.
    /// </summary>
    [Fact]
    public void Getting_it_right_clears_what_was_counted()
    {
        var (throttle, _) = Throttle();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerWindow - 1; attempt++)
        {
            throttle.RecordFailure();
        }

        throttle.RecordSuccess();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerWindow - 1; attempt++)
        {
            Assert.Null(throttle.RetryAfter());
            throttle.RecordFailure();
        }

        Assert.Null(throttle.RetryAfter());
    }

    [Fact]
    public void The_wait_it_reports_is_what_is_left_of_the_window()
    {
        var (throttle, time) = Throttle();

        for (var attempt = 0; attempt < SignInThrottle.AttemptsPerWindow; attempt++)
        {
            throttle.RecordFailure();
        }

        time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(SignInThrottle.Window - TimeSpan.FromMinutes(2), throttle.RetryAfter());
    }
}
