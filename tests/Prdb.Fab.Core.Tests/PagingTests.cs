using Xunit;

namespace Prdb.Fab.Core.Tests;

/// <summary>
/// ADR 0036 puts the page number in the address bar, which means a person can
/// type anything into it. These are the two ends of what they can type.
/// </summary>
public sealed class PagingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    [InlineData(-7, 1)]
    [InlineData(int.MinValue, 1)]
    public void A_page_below_the_first_one_is_the_first_one(int page, int expected) =>
        Assert.Equal(expected, Paging.Wanted(page));

    [Theory]
    [InlineData(1, 48, 0)]
    [InlineData(2, 48, 48)]
    [InlineData(3, 50, 100)]
    [InlineData(0, 48, 0)]
    [InlineData(-7, 48, 0)]
    public void The_offset_is_the_pages_before_it(int page, int size, int expected) =>
        Assert.Equal(expected, Paging.Skip(page, size));

    /// <summary>
    /// The whole reason this is not <c>(page - 1) * pageSize</c> at each call
    /// site: that overflows to a negative offset, which is not an error and is
    /// not an empty page — it is silently the <em>first</em> page, reported
    /// under the number that was asked for.
    /// </summary>
    [Theory]
    [InlineData(int.MaxValue, 48)]
    [InlineData(int.MaxValue, 50)]
    [InlineData(100_000_000, 48)]
    public void A_page_past_what_an_offset_holds_skips_everything(int page, int size)
    {
        var skip = Paging.Skip(page, size);

        Assert.True(skip > 0, $"{skip} is not past the first page");
        Assert.Equal(int.MaxValue, skip);
    }
}
