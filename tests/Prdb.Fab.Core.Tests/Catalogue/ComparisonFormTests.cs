using Prdb.Fab.Core.Catalogue;

using Xunit;

namespace Prdb.Fab.Core.Tests.Catalogue;

/// <summary>
/// ADR 0023's comparison form: lower case, every separator collapsed to one,
/// the extension dropped.
/// </summary>
public sealed class ComparisonFormTests
{
    [Theory]
    [InlineData("Brazzers Exxtra", "brazzers exxtra")]
    [InlineData("BrazzersExxtra.26.08.15.Jane.Doe.XXX.1080p", "brazzersexxtra 26 08 15 jane doe xxx 1080p")]
    [InlineData("A_Title__With---Separators", "a title with separators")]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("(Bracketed) [Tagged]", "bracketed tagged")]
    public void Everything_is_lower_case_with_one_separator(string text, string expected) =>
        Assert.Equal(expected, ComparisonForm.Of(text));

    /// <summary>
    /// The extension goes, because one side of this comparison is a file name
    /// and the other never is.
    /// </summary>
    [Theory]
    [InlineData("Site.26.08.15.Jane.Doe.XXX.1080p.MP4-GROUP.mkv", "site 26 08 15 jane doe xxx 1080p mp4 group")]
    [InlineData("something.mp4", "something")]
    [InlineData("something.webm", "something")]
    public void The_extension_is_dropped(string text, string expected) =>
        Assert.Equal(expected, ComparisonForm.Of(text));

    /// <summary>
    /// And nothing else is. A scene release title ends in a date or a group
    /// often enough that reading either as an extension would quietly shorten
    /// half the needles this tool has.
    /// </summary>
    [Theory]
    [InlineData("Site.26.08.15", "site 26 08 15")]
    [InlineData("Something.MP4-GROUPNAME", "something mp4 groupname")]
    public void Only_something_shaped_like_an_extension_is_dropped(string text, string expected) =>
        Assert.Equal(expected, ComparisonForm.Of(text));

    /// <summary>
    /// What the form is <em>for</em>. ADR 0023 tests containment rather than
    /// equality, because equality would miss almost everything indexers do to a
    /// title — and missing is the expensive direction.
    /// </summary>
    [Fact]
    public void A_pre_name_is_contained_in_the_release_named_after_it()
    {
        var preName = ComparisonForm.Of("BrazzersExxtra.26.08.15.Jane.Doe.XXX");
        var release = ComparisonForm.Of("BrazzersExxtra_26_08_15_Jane_Doe_XXX_1080p_HEVC-GROUP.mkv");

        Assert.Contains(preName, release, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Nothing_to_compare_is_an_empty_form(string? text) =>
        Assert.Equal(string.Empty, ComparisonForm.Of(text));
}
