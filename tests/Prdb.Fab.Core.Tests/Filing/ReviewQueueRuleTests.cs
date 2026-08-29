using Prdb.Fab.Core.Filing;

using Xunit;

namespace Prdb.Fab.Core.Tests.Filing;

public sealed class ReviewQueueRuleTests
{
    [Theory]
    [InlineData(ArrivingFileReason.Unidentified, ReviewQueueAction.FileAs)]
    [InlineData(ArrivingFileReason.Duplicate, ReviewQueueAction.Replace)]
    [InlineData(ArrivingFileReason.EntryMissing, ReviewQueueAction.FileAsOnlyCopy)]
    public void One_reason_has_at_most_one_acting_exit(
        ArrivingFileReason reason,
        ReviewQueueAction expected) =>
        Assert.Equal(expected, ReviewQueueActions.For(reason));

    [Theory]
    [InlineData(ArrivingFileReason.IdenticalFile)]
    [InlineData(ArrivingFileReason.UnreadableQuality)]
    public void A_reason_with_nothing_to_choose_has_no_acting_exit(ArrivingFileReason reason) =>
        Assert.Null(ReviewQueueActions.For(reason));

    [Theory]
    [InlineData("release.nfo")]
    [InlineData("release.PAR2")]
    [InlineData("release.sfv")]
    [InlineData("release.srr")]
    [InlineData("release.url")]
    [InlineData("release.txt")]
    [InlineData("cover.jpg")]
    [InlineData("cover.png")]
    public void The_fixed_leftover_set_is_recognised(string name) =>
        Assert.True(Leftovers.IsSupported(name));

    [Theory]
    [InlineData("video.mkv")]
    [InlineData("archive.rar")]
    [InlineData("subtitle.srt")]
    [InlineData("no-extension")]
    public void Everything_outside_the_fixed_leftover_set_is_refused(string name) =>
        Assert.False(Leftovers.IsSupported(name));
}
