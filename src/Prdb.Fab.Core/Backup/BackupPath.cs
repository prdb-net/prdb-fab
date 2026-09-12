namespace Prdb.Fab.Core.Backup;

/// <summary>
/// Which root a path in the Backup document was written against.
/// </summary>
public enum BackupRoot
{
    /// <summary>
    /// No root covers it, so the path is in the document whole. Not an error:
    /// an installation whose library root has changed since a Video File was
    /// filed holds exactly such a path, and ADR 0017 says the record is the
    /// authority — so it travels as it is rather than being bent to fit.
    /// </summary>
    None,

    /// <summary>Under the Library root.</summary>
    Library,

    /// <summary>Under the Download Directory.</summary>
    Downloads,
}

/// <summary>
/// One path as the Backup document carries it: the root it belongs to, and what
/// is left of it below that root.
/// </summary>
/// <remarks>
/// ADR 0033 puts absolute paths in the database and root-relative ones in the
/// Backup, and this is that second form. It names its root rather than assuming
/// one, because the Operation Log records moves that begin in the Download
/// Directory and end in the Library, so which root a path belongs to is a
/// property of the path and not of the column it came from.
/// </remarks>
/// <param name="Root">The root <paramref name="Path"/> is relative to.</param>
/// <param name="Path">
/// What lies below the root, separated by <c>/</c> whatever the platform writes,
/// or the whole absolute path when <paramref name="Root"/> is
/// <see cref="BackupRoot.None"/>.
/// </param>
public sealed record BackupPath(BackupRoot Root, string Path);

/// <summary>
/// The two roots a Backup is written against, and the conversion in both
/// directions.
/// </summary>
/// <remarks>
/// ADR 0009 re-answers both of these at Restore, prefilled from the
/// configuration, which is the whole reason the document does not carry
/// absolute paths: the container that reads the file may mount its library
/// somewhere else entirely.
/// </remarks>
/// <param name="Library">
/// The Library root, or null on an installation that has not answered it yet.
/// </param>
/// <param name="Downloads">
/// The Download Directory — the local half of the SABnzbd path mapping — or null
/// where SABnzbd was skipped (ADR 0010).
/// </param>
public sealed record BackupRoots(string? Library, string? Downloads)
{
    /// <summary>Neither root answered, which is what a fresh installation has.</summary>
    public static BackupRoots Unanswered { get; } = new(Library: null, Downloads: null);

    /// <summary>
    /// <paramref name="absolute"/> as the document carries it.
    /// </summary>
    /// <remarks>
    /// The longest matching root wins. The schema refuses a Library root that
    /// overlaps the Download Directory (ADR 0020), so in practice at most one
    /// matches; an installation configured before that rule existed is the case
    /// this orders deterministically rather than by which root is tried first.
    /// </remarks>
    public BackupPath Relative(string absolute)
    {
        var below = Below(absolute, Library);
        var belowDownloads = Below(absolute, Downloads);

        if (below is not null && belowDownloads is not null)
        {
            return Trimmed(Library!).Length >= Trimmed(Downloads!).Length
                ? new(BackupRoot.Library, below)
                : new(BackupRoot.Downloads, belowDownloads);
        }

        if (below is not null) return new(BackupRoot.Library, below);

        return belowDownloads is not null
            ? new(BackupRoot.Downloads, belowDownloads)
            : new(BackupRoot.None, absolute);
    }

    /// <summary>
    /// The same path against these roots, or null where the root it names is
    /// not answered — which is a Restore that cannot place the path rather than
    /// one that may guess.
    /// </summary>
    public string? Absolute(BackupPath path)
    {
        if (path.Root == BackupRoot.None) return path.Path;

        var root = path.Root == BackupRoot.Library ? Library : Downloads;

        if (string.IsNullOrWhiteSpace(root)) return null;

        var segments = path.Path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        // A segment that walks upwards would put a restored path outside the
        // root the user just answered, which is the one thing re-rooting must
        // not be able to do.
        return segments.Any(segment => segment is "." or "..")
            ? null
            : segments.Aggregate(Trimmed(root), Path.Combine);
    }

    /// <summary>
    /// What is left of <paramref name="absolute"/> below <paramref name="root"/>,
    /// or null when the root does not cover it.
    /// </summary>
    /// <remarks>
    /// The separator boundary is what stops <c>/data</c> from matching
    /// <c>/database</c> — the same comparison <c>LibraryRoot.Compare</c> and
    /// <c>PathMapping.Resolve</c> make, and for the same reason.
    /// </remarks>
    private static string? Below(string absolute, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        var trimmed = Trimmed(root);
        var path = absolute.Trim();

        if (string.Equals(path, trimmed, StringComparison.Ordinal)) return string.Empty;

        if (path.Length <= trimmed.Length
            || !path.StartsWith(trimmed, StringComparison.Ordinal)
            || !IsSeparator(path[trimmed.Length]))
        {
            return null;
        }

        return string.Join('/', path[(trimmed.Length + 1)..]
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Trimmed(string path) =>
        Path.TrimEndingDirectorySeparator(path.Trim());

    private static bool IsSeparator(char value) => value is '/' or '\\';
}
