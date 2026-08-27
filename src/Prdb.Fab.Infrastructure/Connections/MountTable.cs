namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// Which device a path is on, read from the kernel's own mount table.
/// </summary>
/// <remarks>
/// <para>
/// The parsing is separated from the reading because the case that matters
/// cannot be constructed in a test: two mounts of <em>one</em> filesystem needs
/// a mount, and ADR 0042 already declined to mount a loop device to manufacture
/// what the kernel does. A recorded table is the honest way to test it, and
/// this is the seam that lets one be used.
/// </para>
/// <para>
/// Linux only, deliberately. ADR 0034 ships a Linux container and CI runs on
/// Linux; anywhere else the answer is <em>we could not tell</em>, which is one
/// of the three answers this question already has.
/// </para>
/// </remarks>
public static class MountTable
{
    private const string ProcMountInfo = "/proc/self/mountinfo";

    /// <summary>
    /// The device the deepest mount covering <paramref name="path"/> is on, as
    /// the kernel's <c>major:minor</c> — or null when the table says nothing
    /// about it.
    /// </summary>
    public static string? DeviceOf(string path) =>
        File.Exists(ProcMountInfo) ? DeviceIn(ReadLines(), path) : null;

    /// <summary>
    /// The same question, asked of a table that has already been read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A line of <c>mountinfo</c> is
    /// <c>ID PARENT MAJOR:MINOR ROOT MOUNTPOINT OPTIONS...</c>, and the two
    /// fields wanted here are the third and the fifth. Everything after the
    /// fifth varies by kernel version and is not read.
    /// </para>
    /// <para>
    /// The deepest mount point the path falls under wins, and among equally
    /// deep ones the last, because a later line shadows an earlier one at the
    /// same place — which is what an overmount is.
    /// </para>
    /// </remarks>
    public static string? DeviceIn(IEnumerable<string> table, string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? device = null;
        var deepest = -1;

        foreach (var line in table)
        {
            var fields = line.Split(' ');

            if (fields.Length < 5)
            {
                continue;
            }

            var mountPoint = Path.TrimEndingDirectorySeparator(Unescape(fields[4]));

            if (mountPoint.Length == 0)
            {
                // The root, which every path is under and which trimming leaves
                // empty.
                mountPoint = Path.DirectorySeparatorChar.ToString();
            }

            if (!Under(full, mountPoint) || mountPoint.Length < deepest)
            {
                continue;
            }

            deepest = mountPoint.Length;
            device = fields[2];
        }

        return device;
    }

    private static IEnumerable<string> ReadLines()
    {
        try
        {
            // Read rather than streamed: the file is a snapshot, and holding it
            // open while deciding invites reading a table that changed halfway.
            return File.ReadAllLines(ProcMountInfo);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The kernel escapes the four characters that would otherwise break the
    /// field split. A mount point with a space in it is unusual and is not a
    /// reason to answer wrongly about it.
    /// </summary>
    private static string Unescape(string field)
    {
        if (!field.Contains('\\', StringComparison.Ordinal))
        {
            return field;
        }

        return field
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }

    private static bool Under(string path, string mountPoint) =>
        string.Equals(path, mountPoint, StringComparison.Ordinal)
        || (path.StartsWith(mountPoint, StringComparison.Ordinal)
            && (mountPoint.EndsWith(Path.DirectorySeparatorChar)
                || path[mountPoint.Length] == Path.DirectorySeparatorChar));
}
