using System.Text;

using Prdb.Fab.Core.Filing;

using Xunit;

namespace Prdb.Fab.Core.Tests.Filing;

/// <summary>
/// ADR 0017's layout, as it is computed. The rules were measured against a real
/// media server and an SMB share rather than reasoned, so these assert the
/// measurements rather than restate the code.
/// </summary>
public sealed class EntryPathTests
{
    private static readonly Guid Video = Guid.Parse("018f4f2e-8a4b-7c1d-9e3f-2b6c5d4a1f00");

    private static FiledVideo Filed(
        string site = "Example Site",
        string title = "An Example Title",
        DateOnly? releaseDate = null) =>
        new(Video, site, title, releaseDate ?? new DateOnly(2026, 8, 28));

    [Fact]
    public void The_layout_is_site_then_site_date_and_title()
    {
        var path = EntryPaths.For(Filed(), ".mkv");

        Assert.Equal("Example Site", path.SiteDirectory);
        Assert.Equal("Example Site - 2026-08-28 - An Example Title", path.EntryDirectory);
        Assert.Equal(".mkv", path.Extension);
        Assert.Equal("Example Site - 2026-08-28 - An Example Title.mkv", path.VideoFileName);
    }

    /// <summary>
    /// A missing release date drops its segment, separator and all. A
    /// placeholder would put data-shaped non-data on disk and buy no stability,
    /// since the name changes anyway once the date arrives.
    /// </summary>
    [Fact]
    public void A_missing_release_date_drops_its_segment_rather_than_filling_it()
    {
        var path = EntryPaths.For(Filed(releaseDate: null) with { ReleaseDate = null }, ".mp4");

        Assert.Equal("Example Site - An Example Title", path.EntryDirectory);
        Assert.DoesNotContain("0000", path.EntryDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", path.EntryDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reserved set becomes spaces rather than vanishing, so that `A/B` stays
    /// two words, and runs of whitespace then collapse to one.
    /// </summary>
    [Theory]
    [InlineData("A/B", "A B")]
    [InlineData("Who? What!", "Who What!")]
    [InlineData("a:b|c*d", "a b c d")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData(".hidden", "hidden")]
    [InlineData("trailing. ", "trailing")]
    public void Reserved_and_control_characters_become_spaces(string written, string expected) =>
        Assert.Equal(expected, LibraryNames.Sanitise(written));

    [Fact]
    public void A_control_character_is_a_space_and_a_surrogate_pair_survives()
    {
        Assert.Equal("a b", LibraryNames.Sanitise("ab"));
        Assert.Equal("A 🎬 B", LibraryNames.Sanitise("A 🎬 B"));
    }

    /// <summary>
    /// A title made of nothing but reserved characters sanitises to nothing, and
    /// an empty path component is worse than an ugly one.
    /// </summary>
    [Fact]
    public void A_name_that_sanitises_to_nothing_falls_back_to_the_video_id()
    {
        var path = EntryPaths.For(Filed(site: "///", title: "***"), ".mkv");

        Assert.Equal(Video.ToString("d"), path.SiteDirectory);
        Assert.StartsWith(Video.ToString("d"), path.EntryDirectory, StringComparison.Ordinal);
    }

    /// <summary>
    /// The budget is bytes rather than characters, and the cut falls between
    /// runes: a component truncated mid-sequence is a name some filesystems
    /// refuse rather than merely an ugly one.
    /// </summary>
    [Fact]
    public void A_long_name_is_cut_between_runes_and_within_the_byte_budget()
    {
        var path = EntryPaths.For(Filed(title: new string('あ', 200)), ".mkv");
        var bytes = Encoding.UTF8.GetByteCount(path.EntryDirectory);

        Assert.True(bytes <= LibraryNames.EntryDirectoryBudgetBytes, $"{bytes} bytes");
        Assert.Equal(
            path.EntryDirectory,
            Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(path.EntryDirectory)));

        // The longest name derived from the directory still fits a component.
        var longest = path.VideoFileNameFor("2160p");
        Assert.True(
            Encoding.UTF8.GetByteCount(longest) <= LibraryNames.ComponentBudgetBytes,
            $"{Encoding.UTF8.GetByteCount(longest)} bytes");
    }

    /// <summary>
    /// The room kept free is a constant rather than the extension actually being
    /// filed, or the same video arriving as `.mkv` and as `.mpeg` would produce
    /// two directories.
    /// </summary>
    [Fact]
    public void The_same_video_produces_one_directory_whatever_it_arrives_as()
    {
        var video = Filed(title: new string('x', 400));

        Assert.Equal(
            EntryPaths.For(video, ".mkv").EntryDirectory,
            EntryPaths.For(video, ".mpeg").EntryDirectory);
    }

    [Fact]
    public void What_a_cut_exposes_is_trimmed()
    {
        Assert.Equal("name", LibraryNames.Fit("name - ", 32));
        Assert.Equal("name", LibraryNames.Fit("name_", 32));
        Assert.Equal("na", LibraryNames.Fit("na-me", 3));
    }

    /// <summary>
    /// A file arriving without an extension is given none: inventing `.mkv` would
    /// put a name on disk that lies about what the container is.
    /// </summary>
    [Theory]
    [InlineData(".MKV", ".mkv")]
    [InlineData("mp4", ".mp4")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void The_extension_is_taken_from_the_file_and_lower_cased(
        string? extension,
        string expected) =>
        Assert.Equal(expected, EntryPaths.For(Filed(), extension).Extension);

    /// <summary>
    /// The bracketed label is what groups two files as one entry. A video filed
    /// once keeps the plain name.
    /// </summary>
    [Fact]
    public void A_second_quality_is_named_with_a_bracketed_label()
    {
        var path = EntryPaths.For(Filed(), ".mkv");

        Assert.Equal("Example Site - 2026-08-28 - An Example Title.mkv", path.VideoFileNameFor(null));
        Assert.Equal(
            "Example Site - 2026-08-28 - An Example Title - [2160p].mkv",
            path.VideoFileNameFor("2160p"));
    }

    /// <summary>
    /// A collision is broken with the full video id, because a collision needs
    /// the same site, the same date and the same title.
    /// </summary>
    [Fact]
    public void A_distinguished_name_carries_the_whole_video_id()
    {
        var path = EntryPaths.For(Filed(), ".mkv", distinguish: true);

        Assert.EndsWith($" [{Video:d}]", path.EntryDirectory, StringComparison.Ordinal);
        Assert.True(
            Encoding.UTF8.GetByteCount(path.EntryDirectory)
                <= LibraryNames.EntryDirectoryBudgetBytes,
            path.EntryDirectory);
    }

    /// <summary>
    /// A second Quality is named after the directory that is there, not after
    /// what the layout would produce for the same video today: a name that does
    /// not begin with the directory's own splits one entry into two.
    /// </summary>
    [Fact]
    public void A_recorded_directory_names_what_goes_beside_what_is_in_it()
    {
        var recorded = EntryPath.At("/library/Example Site/Older Name Than Today", ".mp4");

        Assert.Equal("Example Site", recorded.SiteDirectory);
        Assert.Equal("Older Name Than Today", recorded.EntryDirectory);
        Assert.Equal("Older Name Than Today - [1080p].mp4", recorded.VideoFileNameFor("1080p"));
        Assert.Equal(
            Path.Combine("/library", "Example Site", "Older Name Than Today", "movie.nfo"),
            recorded.SidecarUnder("/library"));
        Assert.Equal(
            Path.Combine("/library", "Example Site", "Older Name Than Today", "fanart.jpg"),
            recorded.EntryImageUnder("/library"));
    }
}
