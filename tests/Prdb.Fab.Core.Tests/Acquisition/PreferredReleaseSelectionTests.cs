using Prdb.Fab.Core.Acquisition;

using Xunit;

namespace Prdb.Fab.Core.Tests.Acquisition;

public sealed class PreferredReleaseSelectionTests
{
    [Fact]
    public void The_preferred_quality_wins_before_existing_release_rank()
    {
        var releases = new[]
        {
            new Release("largest.2160p", 1),
            new Release("preferred.1080p", 2),
            new Release("smaller.720p", 3),
        };

        var selected = PreferredReleaseSelection.Best(
            releases,
            PreferredDownloadQuality.P1080,
            release => release.Title);

        Assert.Equal(2, selected!.Rank);
    }

    [Fact]
    public void The_next_lower_quality_is_used_and_a_higher_one_is_not()
    {
        var releases = new[]
        {
            new Release("higher-uhd", 1),
            new Release("fallback.720P", 2),
            new Release("last.480p", 3),
        };

        var selected = PreferredReleaseSelection.Best(
            releases,
            PreferredDownloadQuality.P1080,
            release => release.Title);

        Assert.Equal(2, selected!.Rank);
    }

    [Fact]
    public void Existing_rank_breaks_ties_inside_one_quality()
    {
        var releases = new[]
        {
            new Release("first.1080p", 1),
            new Release("second.fhd", 2),
        };

        var selected = PreferredReleaseSelection.Best(
            releases,
            PreferredDownloadQuality.P1080,
            release => release.Title);

        Assert.Equal(1, selected!.Rank);
    }

    [Fact]
    public void An_unlabelled_release_is_the_last_resort_below_the_ceiling()
    {
        var releases = new[]
        {
            new Release("known.2160p", 1),
            new Release("unlabelled", 2),
        };

        var selected = PreferredReleaseSelection.Best(
            releases,
            PreferredDownloadQuality.P1080,
            release => release.Title);

        Assert.Equal(2, selected!.Rank);
    }

    [Theory]
    [InlineData("video.4K.web", PreferredDownloadQuality.P2160)]
    [InlineData("video.2160p.web", PreferredDownloadQuality.P2160)]
    [InlineData("video-UHD-web", PreferredDownloadQuality.P2160)]
    [InlineData("video.FHD.web", PreferredDownloadQuality.P1080)]
    [InlineData("video.1080p.web", PreferredDownloadQuality.P1080)]
    [InlineData("video.720p.web", PreferredDownloadQuality.P720)]
    [InlineData("video.480p.web", PreferredDownloadQuality.P480)]
    public void Common_release_tags_are_recognised(
        string title,
        PreferredDownloadQuality expected)
    {
        Assert.Equal(expected, PreferredReleaseSelection.QualityOf(title));
    }

    [Theory]
    [InlineData("release-1080")]
    [InlineData("release-2160x1080")]
    [InlineData("release-7200p")]
    public void Bare_dimensions_and_embedded_numbers_are_not_quality_tags(string title)
    {
        Assert.Null(PreferredReleaseSelection.QualityOf(title));
    }

    private sealed record Release(string Title, int Rank);
}
