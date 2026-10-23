using Prdb.Fab.Core.Sync;

using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

/// <summary>
/// ADR 0013's repair pass steered by a request budget rather than by a cadence,
/// and ADR 0014's number for it.
/// </summary>
public sealed class RepairBudgetTests
{
    /// <summary>
    /// The half of ADR 0014's sentence the governor cannot express: repair is
    /// last in the order of precedence, so an allowance that rounded down to
    /// zero would stall it forever with nothing failing — which is the silent
    /// shape ADR 0018 exists to make visible and is cheaper not to create.
    /// </summary>
    [Fact]
    public void A_small_plan_still_spends_one_request_a_run()
    {
        // ADR 0014 holds repair back below twenty percent, so a plan of ten an
        // hour with two left has nothing to give it. It asks anyway; whether
        // the request goes out is the governor's, and a deferral is a Brake.
        Assert.Equal(1, RepairBudget.RequestsFor(new PrdbBudget(10, 2, TimeSpan.Zero)));
        Assert.Equal(1, RepairBudget.RequestsFor(new PrdbBudget(10, 0, TimeSpan.Zero)));
    }

    /// <summary>
    /// Before the first response there is no reading at all, and the request is
    /// how one arrives — the same answer the governor gives to the same
    /// question.
    /// </summary>
    [Fact]
    public void A_budget_nobody_has_read_yet_is_worth_one_request()
    {
        Assert.Equal(RepairBudget.AtLeast, RepairBudget.RequestsFor(null));
        Assert.Equal(RepairBudget.ABatch, RepairBudget.VideosFor(null));
    }

    /// <summary>
    /// What is left above the half line, which is exactly what ADR 0014 says
    /// repair may spend.
    /// </summary>
    [Fact]
    public void What_is_spent_is_what_holds_usage_above_the_reserve_with_a_bounded_turn()
    {
        var budget = new PrdbBudget(Limit: 20, Remaining: 10, TimeSpan.Zero);

        // Four are reserved, six are above the line.
        Assert.Equal(6, RepairBudget.RequestsFor(budget));
        Assert.Equal(6 * RepairBudget.ABatch, RepairBudget.VideosFor(budget));
    }

    /// <summary>
    /// ADR 0032's rule that a run is bounded and yields. A generous plan is
    /// spent over many short turns of the bulk lane rather than in one that
    /// holds it, so the ceiling is on the run and not on the budget.
    /// </summary>
    [Fact]
    public void A_generous_plan_is_spent_over_several_runs()
    {
        Assert.Equal(
            RepairBudget.MostPerRun,
            RepairBudget.RequestsFor(new PrdbBudget(10_000, 10_000, TimeSpan.Zero)));
    }
}
