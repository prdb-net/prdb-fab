using Prdb.Fab.Core.Reporting;

using Xunit;

namespace Prdb.Fab.Core.Tests.Reporting;

public sealed class FulfilmentQualityTests
{
    [Theory]
    [InlineData("2160p", FulfilmentQuality.P2160)]
    [InlineData("1440p", FulfilmentQuality.P1080)]
    [InlineData("1080p", FulfilmentQuality.P1080)]
    [InlineData("720p", FulfilmentQuality.P720)]
    [InlineData("576p", null)]
    [InlineData("480p", null)]
    public void Quality_is_rounded_down_without_inventing_a_rung(
        string label,
        FulfilmentQuality? expected)
    {
        Assert.Equal(expected, FulfilmentQualities.HighestTruthfullyReportable([label]));
    }

    [Fact]
    public void One_entry_reports_the_highest_truth_its_files_support()
    {
        Assert.Equal(
            FulfilmentQuality.P1080,
            FulfilmentQualities.HighestTruthfullyReportable(["720p", "1080p", "1440p"]));
    }
}
