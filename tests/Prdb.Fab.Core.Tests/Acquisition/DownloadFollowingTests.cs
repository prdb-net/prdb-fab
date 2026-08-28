using Prdb.Fab.Core.Acquisition;

using Xunit;

namespace Prdb.Fab.Core.Tests.Acquisition;

public sealed class DownloadFollowingTests
{
    [Fact]
    public void Three_consecutive_successful_absences_are_vanished()
    {
        var first = DownloadFollowing.Absent(0);
        var second = DownloadFollowing.Absent(first.ConsecutiveAbsences);
        var third = DownloadFollowing.Absent(second.ConsecutiveAbsences);

        Assert.Equal(DownloadState.Outstanding, first.State);
        Assert.Equal(DownloadState.Outstanding, second.State);
        Assert.Equal(DownloadState.Failed, third.State);
        Assert.Equal(DownloadCause.Vanished, third.Cause);
    }

    [Theory]
    [InlineData(DownloadSignal.Outstanding, DownloadState.Outstanding, null)]
    [InlineData(DownloadSignal.Completed, DownloadState.Completed, null)]
    [InlineData(DownloadSignal.Failed, DownloadState.Failed, DownloadCause.Failed)]
    [InlineData(DownloadSignal.Unusable, DownloadState.Failed, DownloadCause.Unusable)]
    public void A_found_job_resets_absence_and_uses_its_machine_signal(
        DownloadSignal signal,
        DownloadState state,
        DownloadCause? cause)
    {
        var result = DownloadFollowing.Found(signal);

        Assert.Equal(state, result.State);
        Assert.Equal(cause, result.Cause);
        Assert.Equal(0, result.ConsecutiveAbsences);
    }
}
