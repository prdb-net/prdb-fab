using Prdb.Fab.Core.Connections;

using Xunit;

namespace Prdb.Fab.Core.Tests.Connections;

/// <summary>
/// Which of SABnzbd's own folders a category's downloads finish in. The whole
/// point of asking the category first, and the one thing in ADR 0010's SABnzbd
/// step that is decidable without opening anything.
/// </summary>
public sealed class SabnzbdConnectionTests
{
    [Fact]
    public void A_category_with_no_folder_of_its_own_finishes_in_the_completed_folder() =>
        Assert.Equal(
            "/downloads/complete",
            SabnzbdConnection.CompletedRoot("/downloads/complete", categoryFolder: null));

    [Fact]
    public void An_absolute_category_folder_replaces_the_completed_folder() =>
        Assert.Equal(
            "/mnt/tank/xxx",
            SabnzbdConnection.CompletedRoot("/downloads/complete", "/mnt/tank/xxx"));

    /// <summary>
    /// SABnzbd appends a relative one, and creates it when the first download
    /// for the category finishes. So the folder that is certain to be there is
    /// the one above it — and verifying the one below would refuse a correct
    /// answer on a fresh installation.
    /// </summary>
    [Fact]
    public void A_relative_category_folder_answers_the_folder_above_it() =>
        Assert.Equal(
            "/downloads/complete",
            SabnzbdConnection.CompletedRoot("/downloads/complete", "xxx"));

    /// <summary>
    /// A trailing asterisk is SABnzbd's way of saying <em>no per-job
    /// subfolder</em>. It is not part of the name, and a path carrying one
    /// would never resolve.
    /// </summary>
    [Fact]
    public void A_trailing_asterisk_is_not_part_of_the_folder() =>
        Assert.Equal(
            "/mnt/tank/xxx",
            SabnzbdConnection.CompletedRoot("/downloads/complete", "/mnt/tank/xxx*"));

    /// <summary>
    /// The case the path mapping exists for: SABnzbd on Windows, this container
    /// on Linux. The framework's answer would be about the wrong operating
    /// system, so this rule has its own.
    /// </summary>
    [Fact]
    public void A_windows_folder_is_absolute_even_where_a_backslash_means_nothing() =>
        Assert.Equal(
            @"C:\Users\someone\Videos\Complete",
            SabnzbdConnection.CompletedRoot(
                @"D:\SABnzbd\complete",
                @"C:\Users\someone\Videos\Complete"));

    [Fact]
    public void A_windows_relative_folder_still_answers_the_completed_folder() =>
        Assert.Equal(
            @"D:\SABnzbd\complete",
            SabnzbdConnection.CompletedRoot(@"D:\SABnzbd\complete", "xxx"));

    /// <summary>
    /// The sentence is the reason, and ADR 0043 makes the reason a value. A
    /// verdict added later without one would throw where the user is looking.
    /// </summary>
    [Fact]
    public void Every_verdict_says_something()
    {
        foreach (var outcome in Enum.GetValues<SabnzbdConnectionOutcome>())
        {
            Assert.NotEmpty(SabnzbdConnection.Sentence(outcome));
        }
    }
}
