namespace Prdb.Fab.Core.Connections;

/// <summary>
/// What happened when the library root was checked and stored. ADR 0010 gives
/// this step one path and three checks, one of which warns rather than refuses.
/// </summary>
public enum LibraryRootOutcome
{
    /// <summary>Writable, clear of the download directory, and on the same filesystem as it.</summary>
    Saved,

    /// <summary>
    /// Stored, and worth saying out loud: the library and the download
    /// directory are on different filesystems, so filing is a copy and a delete
    /// rather than a rename. ADR 0010 refuses to refuse this — some NAS layouts
    /// are genuinely like that, and refusing them would be refusing a working
    /// installation.
    /// </summary>
    SavedWithWarning,

    /// <summary>ADR 0033: paths are absolute in the database.</summary>
    NotAbsolute,

    /// <summary>There is no such directory in this container.</summary>
    Missing,

    /// <summary>It is there and the container user cannot write to it.</summary>
    NotWritable,

    /// <summary>The library root is inside the download directory.</summary>
    InsideDownloadDirectory,

    /// <summary>The library root contains the download directory.</summary>
    ContainsDownloadDirectory,
}

/// <summary>How two directories sit relative to one another.</summary>
public enum PathOverlap
{
    None,
    Same,
    Inside,
    Contains,
}

/// <summary>
/// The rules behind the library-root step. What is here is what can be decided
/// without opening anything; whether the directory exists, is writable and
/// shares a filesystem is the other half, and it lives where the filesystem
/// does.
/// </summary>
public static class LibraryRoot
{
    public static string Sentence(LibraryRootOutcome outcome) => outcome switch
    {
        LibraryRootOutcome.Saved =>
            "The library root is there, writable, and clear of the download "
            + "directory.",

        LibraryRootOutcome.SavedWithWarning =>
            "Stored. The library root and the download directory are on "
            + "different filesystems, so filing copies each video and deletes "
            + "the original rather than renaming it. That works, and it is "
            + "slower and needs room for both at once.",

        LibraryRootOutcome.NotAbsolute =>
            "The library root is an absolute path — the one inside this "
            + "container, which is whatever you mounted your library at.",

        LibraryRootOutcome.Missing =>
            "There is no such directory in this container. It is a path inside "
            + "a volume you mounted, not the path on the host.",

        LibraryRootOutcome.NotWritable =>
            "That directory is there and this container cannot write to it. It "
            + "runs as the PUID and PGID you gave it, and that is who has to own "
            + "the library.",

        LibraryRootOutcome.InsideDownloadDirectory =>
            "The library root is inside the download directory. Filing moves "
            + "videos out of what SABnzbd finished, and it cannot move them into "
            + "the place it is moving them out of.",

        LibraryRootOutcome.ContainsDownloadDirectory =>
            "The library root contains the download directory. The library is "
            + "the only directory this tool owns, and what SABnzbd leaves behind "
            + "would be sitting inside it.",

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>
    /// How the library root and the download directory sit relative to one
    /// another. Both directions matter, which is why this answers with a
    /// direction rather than with a boolean.
    /// </summary>
    /// <remarks>
    /// A prefix counts only on a separator boundary, so <c>/data/library</c>
    /// does not contain <c>/data/library-old</c>. That is the same rule the path
    /// mapping needs and the same one that stops <c>/data</c> from matching
    /// <c>/database</c>.
    /// </remarks>
    public static PathOverlap Compare(string libraryRoot, string downloadDirectory)
    {
        var library = Normalised(libraryRoot);
        var downloads = Normalised(downloadDirectory);

        if (string.Equals(library, downloads, StringComparison.Ordinal))
        {
            return PathOverlap.Same;
        }

        if (StartsWithDirectory(library, downloads))
        {
            return PathOverlap.Inside;
        }

        return StartsWithDirectory(downloads, library) ? PathOverlap.Contains : PathOverlap.None;
    }

    /// <summary>The refusal an overlap is, or null when there is none.</summary>
    public static LibraryRootOutcome? Refuse(string libraryRoot, string? downloadDirectory)
    {
        if (!Path.IsPathRooted(libraryRoot))
        {
            return LibraryRootOutcome.NotAbsolute;
        }

        // ADR 0010: when SABnzbd was skipped there is no download directory, and
        // then two of the three checks have nothing to compare against.
        if (string.IsNullOrEmpty(downloadDirectory))
        {
            return null;
        }

        return Compare(libraryRoot, downloadDirectory) switch
        {
            PathOverlap.Same or PathOverlap.Inside => LibraryRootOutcome.InsideDownloadDirectory,
            PathOverlap.Contains => LibraryRootOutcome.ContainsDownloadDirectory,
            _ => null,
        };
    }

    private static string Normalised(string path) =>
        Path.TrimEndingDirectorySeparator(path.Trim());

    private static bool StartsWithDirectory(string path, string ancestor) =>
        path.Length > ancestor.Length
        && path.StartsWith(ancestor, StringComparison.Ordinal)
        && (path[ancestor.Length] == Path.DirectorySeparatorChar
            || path[ancestor.Length] == Path.AltDirectorySeparatorChar);
}
