using Prdb.Fab.Core.Automation;

using Xunit;

namespace Prdb.Fab.Core.Tests.Automation;

public sealed class AutomationRulesTests
{
    [Fact]
    public void An_enabled_rule_needs_an_indexer_and_an_ordered_non_negative_size_range()
    {
        Assert.False(AutomationRules.Validate("Rule", true, null, null, 0).Accepted);
        Assert.False(AutomationRules.Validate("Rule", true, -1, null, 1).Accepted);
        Assert.False(AutomationRules.Validate("Rule", true, 2, 1, 1).Accepted);
        Assert.True(AutomationRules.Validate("Rule", true, 1, 2, 1).Accepted);
    }

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData(null, 1, null, false)]
    [InlineData(1, 1, 2, true)]
    [InlineData(2, 1, 2, true)]
    [InlineData(3, 1, 2, false)]
    public void Unknown_size_only_fits_a_rule_without_size_bounds(
        int? size,
        int? minimum,
        int? maximum,
        bool expected) =>
        Assert.Equal(expected, AutomationRules.SizeFits(size, minimum, maximum));
}
