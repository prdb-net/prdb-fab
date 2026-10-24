using Prdb.Fab.Core.Sync;
using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

public sealed class IdleProfileTests
{
    [Theory]
    [InlineData(100, 1000)]
    [InlineData(1000, 20000)]
    [InlineData(10000, 100000)]
    [InlineData(6, 1000)]
    public void All_published_plans_fit_recurring_polls_and_leave_room_for_work(int hourly, int monthly)
    {
        foreach (var minutes in new[] { 30, 15, 5 })
        {
            var wanted = TimeSpan.FromMinutes(minutes);
            var multiplier = IdleProfile.CadenceMultiplier(hourly, monthly, wanted);
            Assert.True(multiplier >= 1);
            Assert.True(IdleProfile.ScheduledRequestsAnHourFor(wanted) / multiplier <= hourly * .4 + .00001);
            Assert.True(IdleProfile.ScheduledRequestsPerWindowFor(wanted) / multiplier <= monthly * .4 + .00001);
            Assert.True(IdleProfile.ReservedPerWindow(hourly, monthly, wanted) + monthly * .2 < monthly);
        }
    }

    [Fact]
    public void Unknown_limits_do_not_invent_a_plan_and_large_plans_keep_the_normal_intervals()
    {
        Assert.Equal(1, IdleProfile.CadenceMultiplier(null, null));
        Assert.Equal(TimeSpan.FromHours(1), IdleProfile.Adapt(TimeSpan.FromHours(1), 1000, 200000));
    }

    [Fact]
    public void Small_plans_slow_account_feeds_too_instead_of_spending_the_month_while_idle()
    {
        var hourlyAccountPoll = IdleProfile.Adapt(TimeSpan.FromHours(1), 100, 1000);
        Assert.True(hourlyAccountPoll > TimeSpan.FromHours(24));
        Assert.Equal(TimeSpan.FromHours(1), IdleProfile.Adapt(TimeSpan.FromHours(1), 10000, 100000));
    }
}
