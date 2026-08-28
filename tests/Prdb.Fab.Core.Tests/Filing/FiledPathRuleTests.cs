using Prdb.Fab.Core.Filing;

using Xunit;

namespace Prdb.Fab.Core.Tests.Filing;

/// <summary>
/// ADR 0017's decisions about a computed path, which filing asks with a `stat` in
/// hand. Sidestepping is right for a collision and wrong for everything else, so
/// what separates the two is asserted rather than assumed.
/// </summary>
public sealed class FiledPathRuleTests
{
    /// <summary>
    /// A directory that exists and is empty is free: a filing that stopped half
    /// way, or a directory somebody made, is not another video's.
    /// </summary>
    [Theory]
    [InlineData(DirectoryState.Absent)]
    [InlineData(DirectoryState.EmptyDirectory)]
    public void A_free_computed_directory_is_used(DirectoryState computed) =>
        Assert.Equal(
            EntryDirectoryVerdict.Use,
            FiledPaths.For(computed, DirectoryState.Absent));

    [Theory]
    [InlineData(DirectoryState.OccupiedDirectory, DirectoryState.Absent)]
    [InlineData(DirectoryState.NotADirectory, DirectoryState.EmptyDirectory)]
    public void An_occupied_name_is_stepped_around_with_the_video_id(
        DirectoryState computed,
        DirectoryState distinguished) =>
        Assert.Equal(
            EntryDirectoryVerdict.Distinguish,
            FiledPaths.For(computed, distinguished));

    /// <summary>
    /// The distinguished path occupied too files nothing: there is no third name,
    /// and inventing one would put a second entry beside the first.
    /// </summary>
    [Fact]
    public void A_taken_distinguished_name_files_nothing() =>
        Assert.Equal(
            EntryDirectoryVerdict.Refuse,
            FiledPaths.For(DirectoryState.OccupiedDirectory, DirectoryState.OccupiedDirectory));

    /// <summary>
    /// A permissions or mount problem must not quietly produce a second library
    /// beside the first, so an unreadable state refuses even where the other name
    /// is free.
    /// </summary>
    [Theory]
    [InlineData(DirectoryState.Unreadable, DirectoryState.Absent)]
    [InlineData(DirectoryState.OccupiedDirectory, DirectoryState.Unreadable)]
    public void An_unreadable_state_files_nothing(
        DirectoryState computed,
        DirectoryState distinguished) =>
        Assert.Equal(
            EntryDirectoryVerdict.Refuse,
            FiledPaths.For(computed, distinguished));

    /// <summary>
    /// The order is fixed — relabel first, then move the newcomer in — so that an
    /// interruption leaves one correctly labelled file, which is a valid entry.
    /// </summary>
    [Fact]
    public void A_second_quality_relabels_the_copy_already_filed() =>
        Assert.Equal(
            SecondQualityVerdict.RelabelThenFile,
            FiledPaths.ForSecondQuality(RecordedEntryState.FileIsThere));

    /// <summary>
    /// The user tidied up: the newcomer is the only copy again, so it is filed
    /// unlabelled and there is nothing to relabel.
    /// </summary>
    [Fact]
    public void A_tidied_up_directory_takes_the_newcomer_unlabelled() =>
        Assert.Equal(
            SecondQualityVerdict.FileUnlabelled,
            FiledPaths.ForSecondQuality(RecordedEntryState.FileIsGone));

    /// <summary>
    /// A deliberately deleted entry and a mount that silently did not come up
    /// look identical from one `stat`, and the careful side of that confusion is
    /// a Review Queue entry rather than a fresh directory.
    /// </summary>
    [Fact]
    public void A_missing_entry_directory_files_nothing() =>
        Assert.Equal(
            SecondQualityVerdict.EntryMissing,
            FiledPaths.ForSecondQuality(RecordedEntryState.DirectoryIsGone));

    /// <summary>
    /// The temporary name hides from the scanner and from this tool's own walk,
    /// cannot be reached by the version grouping rule, and names the download so
    /// the leftover of an interrupted replace is attributable.
    /// </summary>
    [Fact]
    public void The_temporary_name_hides_and_names_its_download()
    {
        var download = Guid.Parse("018f4f2e-8a4b-7c1d-9e3f-2b6c5d4a1f01");
        var name = FiledPaths.TemporaryName(download);

        Assert.StartsWith(".", name, StringComparison.Ordinal);
        Assert.EndsWith(".part", name, StringComparison.Ordinal);
        Assert.Contains(download.ToString("d"), name, StringComparison.Ordinal);
        Assert.False(VideoFiles.IsSupported(name));
        Assert.False(VideoFiles.IsWorthWalking(name));
    }
}
