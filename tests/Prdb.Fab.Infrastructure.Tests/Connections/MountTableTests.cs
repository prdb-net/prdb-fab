using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Connections;

/// <summary>
/// Ticket 12: which device a path is on, answered from the kernel's own mount
/// table rather than from the mount point a path happens to be reached through.
/// </summary>
/// <remarks>
/// Against recorded tables, because the case that was wrong needs a mount to
/// construct — see <c>Recorded/README.md</c>. The two answers that can be
/// reached without one stay in <see cref="DirectoryTests"/> against real
/// directories.
/// </remarks>
public sealed class MountTableTests
{
    /// <summary>
    /// The bug, as it was found: a container given its downloads and its
    /// library as two bind mounts of one filesystem. Two mount points, one
    /// device — and the old comparison called that two filesystems and told
    /// the user their videos would be copied.
    /// </summary>
    [Fact]
    public void Two_bind_mounts_of_one_filesystem_are_one_device()
    {
        var table = Recorded("mountinfo-container.txt");

        Assert.Equal("9:4", MountTable.DeviceIn(table, "/downloads"));
        Assert.Equal("9:4", MountTable.DeviceIn(table, "/library"));
        Assert.Equal("9:4", MountTable.DeviceIn(table, "/data"));
    }

    /// <summary>A path inside a mount is on that mount's device.</summary>
    [Fact]
    public void A_path_under_a_mount_is_on_its_device()
    {
        var table = Recorded("mountinfo-container.txt");

        Assert.Equal("9:4", MountTable.DeviceIn(table, "/library/Some Studio/A Video"));
    }

    /// <summary>
    /// The deepest mount wins, which is what makes a bind mount inside another
    /// filesystem answer for itself rather than for the one it sits in.
    /// </summary>
    [Fact]
    public void The_deepest_mount_answers()
    {
        var table = Recorded("mountinfo-container.txt");

        // Under the overlay root and not under any of the binds.
        Assert.Equal("9:4", MountTable.DeviceIn(table, "/app"));
        Assert.Equal("0:58", MountTable.DeviceIn(table, "/dev/null"));
    }

    /// <summary>The answer the warning exists for is still reached.</summary>
    [Fact]
    public void Two_paths_on_different_devices_are_different()
    {
        var table = Recorded("mountinfo-two-devices.txt");

        Assert.Equal("8:17", MountTable.DeviceIn(table, "/mnt/library"));
        Assert.Equal("8:33", MountTable.DeviceIn(table, "/mnt/downloads"));
        Assert.NotEqual(
            MountTable.DeviceIn(table, "/mnt/library"),
            MountTable.DeviceIn(table, "/mnt/downloads"));
    }

    /// <summary>
    /// A prefix that is not a path component is not a mount. `/mnt/library`
    /// does not cover `/mnt/library-old`, for the same reason `/data/library`
    /// does not contain `/data/library-old`.
    /// </summary>
    [Fact]
    public void A_prefix_that_is_not_a_component_is_not_the_mount()
    {
        var table = Recorded("mountinfo-two-devices.txt");

        Assert.Equal("259:2", MountTable.DeviceIn(table, "/mnt/library-old"));
    }

    /// <summary>
    /// A later line at the same place shadows an earlier one, which is what an
    /// overmount is.
    /// </summary>
    [Fact]
    public void A_mount_over_another_at_the_same_place_wins()
    {
        string[] table =
        [
            "25 0 259:2 / / rw,relatime - ext4 /dev/nvme0n1p2 rw",
            "41 25 8:17 / /mnt/library rw,relatime - xfs /dev/sdb1 rw",
            "62 25 8:49 / /mnt/library rw,relatime - ext4 /dev/sdd1 rw",
        ];

        Assert.Equal("8:49", MountTable.DeviceIn(table, "/mnt/library"));
    }

    /// <summary>
    /// The kernel escapes the four characters that would otherwise break the
    /// field split. A mount point with a space in it is unusual and is not a
    /// reason to answer wrongly about it.
    /// </summary>
    [Fact]
    public void A_mount_point_with_a_space_in_it_is_read_whole()
    {
        string[] table =
        [
            "25 0 259:2 / / rw,relatime - ext4 /dev/nvme0n1p2 rw",
            "41 25 8:17 / /mnt/my\\040library rw,relatime - xfs /dev/sdb1 rw",
        ];

        Assert.Equal("8:17", MountTable.DeviceIn(table, "/mnt/my library"));
    }

    /// <summary>
    /// A table that says nothing about the path is the third answer, and the
    /// library step treats it as "do not warn" rather than as a failure.
    /// </summary>
    [Fact]
    public void A_table_with_nothing_in_it_answers_nothing()
    {
        Assert.Null(MountTable.DeviceIn([], "/library"));
        Assert.Null(MountTable.DeviceIn(["not a mountinfo line"], "/library"));
    }

    private static string[] Recorded(string name) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Connections", "Recorded", name));
}
