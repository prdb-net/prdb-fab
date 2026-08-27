using Prdb.Fab.Core.Connections;

using Xunit;

namespace Prdb.Fab.Core.Tests.Connections;

/// <summary>
/// ADR 0010 refuses a library root that lies inside the download directory or
/// contains it. Both directions, which is why the rule answers with one.
/// </summary>
public sealed class LibraryRootTests
{
    [Fact]
    public void A_library_beside_the_downloads_overlaps_neither_way() =>
        Assert.Equal(PathOverlap.None, LibraryRoot.Compare("/data/library", "/data/downloads"));

    [Fact]
    public void A_library_under_the_downloads_is_inside_it() =>
        Assert.Equal(PathOverlap.Inside, LibraryRoot.Compare("/data/downloads/library", "/data/downloads"));

    [Fact]
    public void A_library_above_the_downloads_contains_them() =>
        Assert.Equal(PathOverlap.Contains, LibraryRoot.Compare("/data", "/data/downloads"));

    [Fact]
    public void The_same_directory_twice_is_the_same_directory() =>
        Assert.Equal(PathOverlap.Same, LibraryRoot.Compare("/data/downloads", "/data/downloads/"));

    /// <summary>
    /// The rule that stops <c>/data</c> from matching <c>/database</c>, and the
    /// same one the path mapping needs. A prefix counts on a separator boundary
    /// and nowhere else.
    /// </summary>
    [Fact]
    public void A_prefix_that_is_not_a_directory_is_not_a_prefix() =>
        Assert.Equal(PathOverlap.None, LibraryRoot.Compare("/data/library-old", "/data/library"));

    [Fact]
    public void An_overlap_is_refused_in_both_directions()
    {
        Assert.Equal(
            LibraryRootOutcome.InsideDownloadDirectory,
            LibraryRoot.Refuse("/data/downloads/library", "/data/downloads"));

        Assert.Equal(
            LibraryRootOutcome.ContainsDownloadDirectory,
            LibraryRoot.Refuse("/data", "/data/downloads"));
    }

    [Fact]
    public void A_relative_library_root_is_refused_before_anything_else() =>
        Assert.Equal(LibraryRootOutcome.NotAbsolute, LibraryRoot.Refuse("library", "/data/downloads"));

    /// <summary>
    /// ADR 0010: when SABnzbd is skipped there is no download directory, and
    /// then two of the three checks have nothing to compare against. They are
    /// skipped rather than answered pessimistically.
    /// </summary>
    [Fact]
    public void Without_a_download_directory_there_is_nothing_to_overlap() =>
        Assert.Null(LibraryRoot.Refuse("/data/library", downloadDirectory: null));

    [Fact]
    public void Every_verdict_says_something()
    {
        foreach (var outcome in Enum.GetValues<LibraryRootOutcome>())
        {
            Assert.NotEmpty(LibraryRoot.Sentence(outcome));
        }
    }
}
