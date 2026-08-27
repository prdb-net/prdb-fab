namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The questions onboarding asks about a directory that only the filesystem can
/// answer. ADR 0035 keeps every one of them on this side of the boundary; the
/// rules that read the answers are in <c>Prdb.Fab.Core</c>.
/// </summary>
/// <remarks>
/// All three are asked by doing the thing rather than by reading a permission
/// bit. ADR 0034 runs the container as <c>PUID:PGID</c>, and what a mode bit
/// says about that user is a longer argument than opening the directory and
/// finding out — over NFS and inside an overlay it is also frequently wrong.
/// </remarks>
public static class Directories
{
    public static bool Exists(string path) => Directory.Exists(path);

    /// <summary>Whether this container can list what is in there.</summary>
    public static bool IsReadable(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();

            return true;
        }
        catch (Exception refused) when (refused is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Whether this container can put a file in there, proven by doing it.</summary>
    public static bool IsWritable(string path)
    {
        var probe = Path.Combine(path, $".prdb-fab-write-probe-{Guid.NewGuid():n}");

        try
        {
            using (File.Create(probe, bufferSize: 1, FileOptions.DeleteOnClose))
            {
                return true;
            }
        }
        catch (Exception refused) when (refused is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether two directories sit on the same filesystem, so that filing can
    /// rename rather than copy and delete.
    /// </summary>
    /// <remarks>
    /// Answered by finding the mount each path falls under — the longest mounted
    /// root that is a prefix of it — rather than by attempting a rename. ADR
    /// 0042 put the kernel's own <c>EXDEV</c> on the list of what is not tested
    /// for the same reason it is not provoked here: it is a property of the
    /// operating system, and provoking it means writing into somebody's library
    /// to find out.
    /// <para>
    /// Null when the mounts cannot be told apart, which is an answer and not a
    /// gap: ADR 0010 warns rather than refuses here, so <em>we could not tell</em>
    /// and <em>they are the same</em> lead to the same act. It is returned as
    /// its own value so that the sentence a person reads is not a guess.
    /// </para>
    /// </remarks>
    public static bool? OnTheSameFilesystem(string first, string second)
    {
        var one = MountOf(first);
        var other = MountOf(second);

        return one is null || other is null ? null : string.Equals(one, other, StringComparison.Ordinal);
    }

    private static string? MountOf(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? deepest = null;

        foreach (var drive in DriveInfo.GetDrives())
        {
            string root;

            try
            {
                root = Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName);
            }
            catch (Exception unreadable) when (unreadable is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            if (root.Length == 0)
            {
                // The filesystem root itself, which every path is under.
                root = Path.DirectorySeparatorChar.ToString();
            }

            if (!Under(full, root))
            {
                continue;
            }

            if (deepest is null || root.Length > deepest.Length)
            {
                deepest = root;
            }
        }

        return deepest;
    }

    private static bool Under(string path, string root) =>
        string.Equals(path, root, StringComparison.Ordinal)
        || (path.StartsWith(root, StringComparison.Ordinal)
            && (root.EndsWith(Path.DirectorySeparatorChar)
                || path[root.Length] == Path.DirectorySeparatorChar));
}
