using Prdb.Fab.Core;

using Xunit;

namespace Prdb.Fab.Core.Tests;

public sealed class SearchPatternTests
{
    [Theory]
    [InlineData(" scene ", "scene", "scene%", "%scene%")]
    [InlineData(@"50%_off\today", @"50\%\_off\\today", @"50\%\_off\\today%", @"%50\%\_off\\today%")]
    public void Literal_patterns_trim_and_escape_like_wildcards(
        string supplied,
        string matching,
        string starting,
        string containing)
    {
        Assert.Equal(matching, SearchPattern.Matching(supplied));
        Assert.Equal(starting, SearchPattern.Starting(supplied));
        Assert.Equal(containing, SearchPattern.Containing(supplied));
    }
}
