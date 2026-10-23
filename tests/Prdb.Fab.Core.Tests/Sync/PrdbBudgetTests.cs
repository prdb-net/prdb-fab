using Prdb.Fab.Core.Sync;
using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

public sealed class PrdbBudgetTests
{
    [Theory]
    [InlineData(1000, 200)]
    [InlineData(100, 20)]
    [InlineData(6, 2)]
    public void Every_unattended_request_preserves_twenty_percent(int limit, int reserve)
    {
        foreach (var work in Enum.GetValues<PrdbWork>().Except([PrdbWork.Verification, PrdbWork.BudgetReading]))
        {
            Assert.False(new PrdbBudget(limit, reserve, TimeSpan.Zero).Admits(work));
            Assert.True(new PrdbBudget(limit, reserve + 1, TimeSpan.Zero).Admits(work));
        }
    }

    [Fact]
    public void Explicit_key_checks_remain_a_recovery_path()
    {
        var budget = new PrdbBudget(1000, 1, TimeSpan.Zero);
        Assert.True(budget.Admits(PrdbWork.Verification));
        Assert.True(budget.Admits(PrdbWork.BudgetReading));
        Assert.False(budget.Admits(PrdbWork.Identification));
    }

    [Fact]
    public void Waits_respect_oldest_slot_expiry_and_do_not_assume_a_full_reset()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), new PrdbBudget(100, 20, TimeSpan.FromSeconds(2)).WaitBefore(PrdbWork.Repair));
        Assert.Equal(TimeSpan.FromMinutes(41), new PrdbBudget(100, 20, TimeSpan.FromMinutes(41)).WaitBefore(PrdbWork.Repair));
        Assert.Equal(TimeSpan.Zero, new PrdbBudget(100, 21, TimeSpan.FromMinutes(41)).WaitBefore(PrdbWork.Identification));
    }
}
