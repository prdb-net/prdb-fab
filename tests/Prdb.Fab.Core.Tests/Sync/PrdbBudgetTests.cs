using Prdb.Fab.Core.Sync;

using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

/// <summary>
/// ADR 0014's governor, as the rule that reads a budget nobody was given the
/// number of.
/// </summary>
public sealed class PrdbBudgetTests
{
    /// <summary>
    /// The order of precedence, exercised as what it is for: as the budget
    /// runs down, the kinds of work stop in ADR 0014's order and not in
    /// another one.
    /// </summary>
    [Fact]
    public void Work_is_given_up_in_the_stated_order()
    {
        var order = Enum.GetValues<PrdbWork>();

        // At every level of a spent budget, whatever is still admitted is a
        // prefix of the order: nothing lower may be sent while something above
        // it is being held back.
        for (var remaining = 0; remaining <= 1000; remaining += 10)
        {
            var budget = new PrdbBudget(Limit: 1000, remaining, TimeSpan.FromMinutes(1));

            var admitted = order.Select(budget.Admits).ToArray();

            Assert.Equal(
                admitted.OrderByDescending(yes => yes),
                admitted);
        }
    }

    /// <summary>
    /// ADR 0014's one number: repair may spend whatever holds hourly usage
    /// under half of the limit.
    /// </summary>
    [Fact]
    public void Repair_stops_at_half_the_limit()
    {
        Assert.True(new PrdbBudget(1000, 501, TimeSpan.Zero).Admits(PrdbWork.Repair));
        Assert.False(new PrdbBudget(1000, 500, TimeSpan.Zero).Admits(PrdbWork.Repair));

        // And it is a share rather than a count, so a small plan is bounded the
        // same way rather than being bounded out of existence.
        Assert.True(new PrdbBudget(20, 11, TimeSpan.Zero).Admits(PrdbWork.Repair));
        Assert.False(new PrdbBudget(20, 10, TimeSpan.Zero).Admits(PrdbWork.Repair));
    }

    /// <summary>
    /// The two nothing is held back from: a person waiting on a key, and a file
    /// that has arrived and cannot be filed until prdb answers. Everything else
    /// has stopped long before this.
    /// </summary>
    [Fact]
    public void The_last_request_of_the_hour_goes_to_an_arrived_file()
    {
        var almostSpent = new PrdbBudget(1000, 1, TimeSpan.FromMinutes(5));

        Assert.True(almostSpent.Admits(PrdbWork.Verification));
        Assert.True(almostSpent.Admits(PrdbWork.Identification));
        Assert.False(almostSpent.Admits(PrdbWork.Writes));
        Assert.False(almostSpent.Admits(PrdbWork.Repair));
    }

    /// <summary>And a spent one admits nothing at all.</summary>
    [Fact]
    public void A_spent_budget_admits_nothing()
    {
        var spent = new PrdbBudget(1000, 0, TimeSpan.FromMinutes(5));

        Assert.All(Enum.GetValues<PrdbWork>(), work => Assert.False(spent.Admits(work)));
    }

    /// <summary>
    /// What a deferred routine waits. Never less than a minute, or a lane
    /// refused at the bottom of the staircase comes back every tick to be
    /// refused again; otherwise the soonest a slot can free up, which is what
    /// prdb reports.
    /// </summary>
    [Fact]
    public void A_deferral_waits_for_a_slot_and_never_less_than_a_minute()
    {
        var soon = new PrdbBudget(1000, 0, TimeSpan.FromSeconds(2));
        Assert.Equal(PrdbBudget.ShortestWait, soon.WaitBefore(PrdbWork.Repair));

        var later = new PrdbBudget(1000, 0, TimeSpan.FromMinutes(41));
        Assert.Equal(TimeSpan.FromMinutes(41), later.WaitBefore(PrdbWork.Repair));

        // Nothing waits for what it is allowed to do.
        var thin = new PrdbBudget(1000, 5, TimeSpan.FromMinutes(41));
        Assert.Equal(TimeSpan.Zero, thin.WaitBefore(PrdbWork.Identification));
    }
}
