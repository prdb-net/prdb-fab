using Prdb.Fab.Core.Backup;

using Xunit;

namespace Prdb.Fab.Core.Tests.Backup;

/// <summary>
/// ADR 0033's second half of the path rule: absolute in the database,
/// root-relative in the Backup. What is asserted here is the conversion and,
/// twice over, what it refuses to do.
/// </summary>
public sealed class BackupRootsTests
{
    private static readonly BackupRoots Roots = new("/library", "/downloads");

    [Fact]
    public void A_path_under_a_root_is_carried_relative_to_it()
    {
        Assert.Equal(
            new BackupPath(BackupRoot.Library, "A Site/A Site - 2026-08-28 - A Title/video.mkv"),
            Roots.Relative("/library/A Site/A Site - 2026-08-28 - A Title/video.mkv"));

        Assert.Equal(
            new BackupPath(BackupRoot.Downloads, "A.Release.1080p/video.mkv"),
            Roots.Relative("/downloads/A.Release.1080p/video.mkv"));
    }

    /// <summary>
    /// The Operation Log's reason for carrying the root per path: one act moves
    /// a file out of the Download Directory and into the Library, so its two
    /// paths belong to two different roots.
    /// </summary>
    [Fact]
    public void Two_paths_of_one_move_name_two_roots()
    {
        Assert.Equal(BackupRoot.Downloads, Roots.Relative("/downloads/job/video.mkv").Root);
        Assert.Equal(BackupRoot.Library, Roots.Relative("/library/A Site/entry/video.mkv").Root);
    }

    /// <summary>
    /// The comparison <c>LibraryRoot.Compare</c> and <c>PathMapping.Resolve</c>
    /// both make: a root matches at a separator or not at all.
    /// </summary>
    [Fact]
    public void A_root_does_not_match_a_directory_that_merely_starts_with_it()
    {
        var roots = new BackupRoots("/data", Downloads: null);

        Assert.Equal(new BackupPath(BackupRoot.None, "/database/video.mkv"), roots.Relative("/database/video.mkv"));
        Assert.Equal(new BackupPath(BackupRoot.Library, "video.mkv"), roots.Relative("/data/video.mkv"));
    }

    /// <summary>
    /// A path no root covers travels whole. An installation whose Library root
    /// was changed after a Video File was filed holds exactly such a path, and
    /// ADR 0017 makes the record the authority — so the document says what the
    /// record says rather than bending the path to fit a root it never had.
    /// </summary>
    [Fact]
    public void A_path_outside_every_root_is_carried_whole()
    {
        Assert.Equal(
            new BackupPath(BackupRoot.None, "/somewhere/else/video.mkv"),
            Roots.Relative("/somewhere/else/video.mkv"));

        Assert.Equal(
            new BackupPath(BackupRoot.None, "/library/video.mkv"),
            BackupRoots.Unanswered.Relative("/library/video.mkv"));
    }

    /// <summary>
    /// Nested roots are refused by the Library-root step (ADR 0020), so this is
    /// about an installation configured before that rule: the answer is the same
    /// every time rather than dependent on which root is compared first.
    /// </summary>
    [Fact]
    public void The_longer_of_two_matching_roots_wins()
    {
        var nested = new BackupRoots("/media/library", "/media");

        Assert.Equal(
            new BackupPath(BackupRoot.Library, "entry/video.mkv"),
            nested.Relative("/media/library/entry/video.mkv"));
    }

    [Fact]
    public void A_root_relative_path_becomes_absolute_again()
    {
        var path = Roots.Relative("/library/A Site/entry/video.mkv");

        Assert.Equal("/library/A Site/entry/video.mkv", Roots.Absolute(path));
        Assert.Equal("/elsewhere/A Site/entry/video.mkv", new BackupRoots("/elsewhere", null).Absolute(path));
    }

    /// <summary>
    /// The two refusals. A root the installation has not answered cannot place
    /// the path, and a path that climbs out of its root is the one thing
    /// re-rooting must not be able to do — ADR 0009 re-answers the roots at
    /// Restore, which is exactly when a document from elsewhere is being read.
    /// </summary>
    [Fact]
    public void A_path_that_cannot_be_placed_honestly_is_not_placed()
    {
        Assert.Null(BackupRoots.Unanswered.Absolute(new BackupPath(BackupRoot.Library, "entry/video.mkv")));
        Assert.Null(Roots.Absolute(new BackupPath(BackupRoot.Library, "../etc/passwd")));
        Assert.Equal("/outside/video.mkv", Roots.Absolute(new BackupPath(BackupRoot.None, "/outside/video.mkv")));
    }

    /// <summary>
    /// A trailing separator on a root is the same root. It arrives from a user
    /// typing one into the Library root form, and it must not turn every path
    /// under it into one no root covers.
    /// </summary>
    [Fact]
    public void A_trailing_separator_on_a_root_changes_nothing()
    {
        var trailing = new BackupRoots("/library/", "/downloads/");

        Assert.Equal(
            new BackupPath(BackupRoot.Library, "entry/video.mkv"),
            trailing.Relative("/library/entry/video.mkv"));
        Assert.Equal("/library/entry/video.mkv", trailing.Absolute(new BackupPath(BackupRoot.Library, "entry/video.mkv")));
    }
}
