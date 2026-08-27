using Prdb.Fab.Core.Scheduling;

using Xunit;

namespace Prdb.Fab.Core.Tests;

/// <summary>
/// ADR 0032's rule, where ADR 0038 asked for it to live: in one place, so that
/// no routine has to remember it.
/// </summary>
public sealed class RunResultTests
{
    [Fact]
    public void An_empty_tick_is_not_a_run()
    {
        Assert.False(RunResult.NothingToDo.IsRecorded);
        Assert.Null(RunResult.NothingToDo.Outcome);
    }

    [Fact]
    public void Every_other_result_is_recorded()
    {
        Assert.True(RunResult.Handled(3).IsRecorded);
        Assert.True(RunResult.Failed("the disk is full").IsRecorded);
        Assert.True(RunResult.Interrupted(3).IsRecorded);
    }

    /// <summary>
    /// ADR 0038: an interrupted run is neither a success nor a failure. What
    /// this asserts is that the three are genuinely three — a two-valued type
    /// would have forced the third into one of the others, which is the
    /// collapse that decision refused.
    /// </summary>
    [Fact]
    public void The_three_outcomes_are_distinct()
    {
        Assert.Equal(RunOutcome.Succeeded, RunResult.Handled(1).Outcome);
        Assert.Equal(RunOutcome.Failed, RunResult.Failed("no").Outcome);
        Assert.Equal(RunOutcome.Interrupted, RunResult.Interrupted(1).Outcome);
    }

    /// <summary>
    /// ADR 0038: an interrupted run keeps what it got through, because that is
    /// the part worth having when a three-hour run is cut short by a restart.
    /// </summary>
    [Fact]
    public void An_interrupted_run_keeps_what_it_finished()
    {
        Assert.Equal(7, RunResult.Interrupted(7).ItemsHandled);
    }

    /// <summary>
    /// ADR 0016 and ADR 0043: a reason is for a reader. Nothing reads it back,
    /// which is why a failure carries a sentence rather than a code.
    /// </summary>
    [Fact]
    public void A_failure_carries_its_reason_and_a_success_does_not()
    {
        Assert.Equal("the disk is full", RunResult.Failed("the disk is full").Reason);
        Assert.Null(RunResult.Handled(1).Reason);
    }
}
