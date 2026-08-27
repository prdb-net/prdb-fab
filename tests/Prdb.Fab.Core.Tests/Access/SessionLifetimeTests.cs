using Prdb.Fab.Core.Access;

using Xunit;

namespace Prdb.Fab.Core.Tests.Access;

public sealed class SessionLifetimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_session_lasts_thirty_days()
    {
        Assert.Equal(Now.AddDays(30), SessionLifetime.ExpiresAt(Now));
    }

    [Fact]
    public void An_expired_session_is_not_usable()
    {
        Assert.False(SessionLifetime.IsUsable(Now.AddSeconds(-1), Now));
        Assert.True(SessionLifetime.IsUsable(Now.AddSeconds(1), Now));
    }

    /// <summary>
    /// Extending on every request would put the browser polling a page in front
    /// of SQLite's single writer. Rationed to once a day, which is invisible to
    /// anyone who has not stopped using the tool for a month.
    /// </summary>
    [Fact]
    public void A_fresh_session_is_not_written_again_straight_away()
    {
        var expiresAt = SessionLifetime.ExpiresAt(Now);

        Assert.False(SessionLifetime.ShouldExtend(expiresAt, Now));
        Assert.False(SessionLifetime.ShouldExtend(expiresAt, Now.AddHours(23)));
        Assert.True(SessionLifetime.ShouldExtend(expiresAt, Now.AddHours(25)));
    }

    [Fact]
    public void An_expired_session_is_never_extended()
    {
        Assert.False(SessionLifetime.ShouldExtend(Now.AddSeconds(-1), Now));
    }
}
