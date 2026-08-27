namespace Prdb.Fab.Core.Connections;

/// <summary>
/// What happened when SABnzbd was checked and stored.
/// </summary>
public enum SabnzbdConnectionOutcome
{
    /// <summary>The key carried, the category exists, and the mapping resolves.</summary>
    Saved,

    /// <summary>
    /// SABnzbd refused the key. ADR 0010 requires a call that actually carries
    /// one, because <c>version</c> and <c>auth</c> answer without it.
    /// </summary>
    WrongKey,

    /// <summary>
    /// The key was not the problem: SABnzbd refused the request because of
    /// where it came from. Its <em>External internet access</em> setting decides
    /// that, and it is checked before the key is even looked at.
    /// </summary>
    AccessDenied,

    /// <summary>Something answered, and it was not SABnzbd's API.</summary>
    NotSabnzbd,

    /// <summary>Nothing answered: a timeout, a refused connection, a wrong port.</summary>
    NotRightNow,

    /// <summary>
    /// The category is no longer one of SABnzbd's own. Never typed, always
    /// chosen — an unknown category is not an error to SABnzbd, it silently
    /// becomes Default and the downloads land somewhere else.
    /// </summary>
    UnknownCategory,

    /// <summary>
    /// The mapping resolves to a path that is not here. ADR 0010: a wrong
    /// mapping is otherwise discovered at the first finished download, where it
    /// presents as a download that hangs.
    /// </summary>
    DownloadDirectoryMissing,

    /// <summary>The path is here and this container cannot read it.</summary>
    DownloadDirectoryUnreadable,
}

/// <summary>
/// The rules behind SABnzbd's step: which of its own folders a category's
/// finished downloads land under, and what each verdict says.
/// </summary>
public static class SabnzbdConnection
{
    public static string Sentence(SabnzbdConnectionOutcome outcome) => outcome switch
    {
        SabnzbdConnectionOutcome.Saved =>
            "SABnzbd answered, the category is one of its own, and the mapped "
            + "folder is here and readable.",

        SabnzbdConnectionOutcome.WrongKey =>
            "SABnzbd refused that key. It has to be the full API key from "
            + "Config \u2192 General \u2014 the NZB key can submit downloads but "
            + "cannot be used to follow them.",

        SabnzbdConnectionOutcome.AccessDenied =>
            "SABnzbd refused the request because of where it came from rather "
            + "than because of the key. Its External internet access setting, "
            + "under Config \u2192 Special, decides that.",

        SabnzbdConnectionOutcome.NotSabnzbd =>
            "Something answered at that address, and it was not SABnzbd's API. "
            + "The address is the one you open SABnzbd at, without /api on the "
            + "end.",

        SabnzbdConnectionOutcome.NotRightNow =>
            "Nothing answered at that address. That is SABnzbd, the port or the "
            + "network rather than the key.",

        SabnzbdConnectionOutcome.UnknownCategory =>
            "SABnzbd no longer has that category. It has to be one of its own: "
            + "one it does not know is not an error there, it quietly becomes "
            + "Default and the downloads land somewhere this tool is not "
            + "looking.",

        SabnzbdConnectionOutcome.DownloadDirectoryMissing =>
            "There is no such folder in this container. This is the half of the "
            + "mapping that this tool has to be able to open, so it is a path "
            + "inside a volume you mounted, not the path SABnzbd shows.",

        SabnzbdConnectionOutcome.DownloadDirectoryUnreadable =>
            "That folder is here and this container cannot read it. It runs as "
            + "the PUID and PGID you gave it, and that is who has to be able to "
            + "read what SABnzbd finished.",

        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    /// <summary>
    /// The folder SABnzbd puts a category's finished downloads under, as far up
    /// as SABnzbd itself guarantees the folder exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SABnzbd's rule is that a category's folder is used as it stands when it
    /// is absolute, appended to the completed-downloads folder when it is
    /// relative, and that a trailing asterisk means <em>no per-job subfolder</em>
    /// rather than being part of the name.
    /// </para>
    /// <para>
    /// A relative one deliberately answers the completed-downloads folder rather
    /// than the subfolder underneath it: SABnzbd creates that subfolder when the
    /// first download for the category finishes, so verifying it on a fresh
    /// installation would refuse a correct answer. What is verified is the
    /// deepest folder that is certain to be there, and the mapping is a prefix,
    /// so the rest of the path rides along with it.
    /// </para>
    /// </remarks>
    public static string CompletedRoot(string completedDownloadsFolder, string? categoryFolder)
    {
        var folder = (categoryFolder ?? string.Empty).Trim();

        if (folder.EndsWith('*'))
        {
            folder = folder[..^1];
        }

        return IsAbsolute(folder) ? folder : completedDownloadsFolder;
    }

    /// <summary>
    /// Whether a path SABnzbd reported is absolute — in SABnzbd's own
    /// filesystem, which need not be this one.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than <c>Path.IsPathRooted</c>, and the reason is the
    /// whole point of a path mapping: SABnzbd may be running on Windows while
    /// this container is Linux, and then the framework's answer is about the
    /// wrong operating system. <c>C:\downloads</c> is absolute even where a
    /// backslash means nothing.
    /// </remarks>
    private static bool IsAbsolute(string path) =>
        path.StartsWith('/')
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || (path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && path[2] is '\\' or '/');
}
