using System.Collections;
using System.Reflection;

namespace Prdb.Fab.Core.Backup;

/// <summary>
/// What happened to a Restore. ADR 0040: a refusal is something the tool
/// checked and can answer, so every one of these is a success with a name in it.
/// </summary>
public enum RestoreOutcome
{
    /// <summary>The installation is the one in the file.</summary>
    Restored,

    /// <summary>
    /// ADR 0010's window is shut: a password exists here, so this is no longer
    /// one of the two writes anybody may make without being signed in.
    /// </summary>
    NotOffered,

    /// <summary>The file is not a Backup.</summary>
    NotABackup,

    /// <summary>Written by a newer tool, named rather than partially read.</summary>
    FromANewerTool,

    /// <summary>
    /// The file is good and the installation is empty. What is missing is the
    /// answer ADR 0009 asks for once: where the Library and the Download
    /// Directory are <em>on this machine</em>. Not a refusal — the first half of
    /// a Restore that is going to work.
    /// </summary>
    RootsNeeded,

    /// <summary>
    /// Something is already here. ADR 0009 refuses and names what it found,
    /// because "not empty" without the reason is a dead end for whoever is
    /// holding the file.
    /// </summary>
    NotEmpty,

    /// <summary>The Library root that was answered cannot be used, and why.</summary>
    LibraryRootRefused,

    /// <summary>
    /// The Download Directory that was answered cannot be used, and why. Asked
    /// for only where the document has paths under it.
    /// </summary>
    DownloadDirectoryRefused,

    /// <summary>
    /// A path in the document does not sit under the root it names — a segment
    /// climbing out of it, or a root the file uses and nobody answered. Nothing
    /// is written: re-rooting is the one step that must not be able to put a
    /// restored path somewhere the person did not point at.
    /// </summary>
    PathOutsideItsRoot,
}

/// <summary>
/// Why one of the two roots a Restore is answered with cannot be used.
/// </summary>
/// <remarks>
/// Its own set rather than <c>LibraryRootOutcome</c>, which ADR 0010 wrote for
/// the onboarding step and whose sentences say "library root" in as many words.
/// A Restore asks the same questions of two directories, and the answer has to
/// be able to name whichever one it is about.
/// </remarks>
public enum RootRefusal
{
    /// <summary>The document has paths under this root and nobody gave one.</summary>
    Unanswered,

    /// <summary>ADR 0033: paths are absolute in the database.</summary>
    NotAbsolute,

    /// <summary>There is no such directory in this container.</summary>
    Missing,

    /// <summary>It is there, and this container cannot list what is in it.</summary>
    NotReadable,

    /// <summary>It is there, and this container cannot write into it.</summary>
    NotWritable,

    /// <summary>The two roots are the same directory, which ADR 0010 refuses.</summary>
    TheSameAsTheOther,

    /// <summary>One root lies inside the other, which ADR 0010 refuses.</summary>
    OverlapsTheOther,
}

/// <summary>
/// The rules of a Restore that do not need a database: the sentence each
/// outcome reads as, and which paths in a document have to be placed.
/// </summary>
public static class Restore
{
    /// <summary>The sentence for a root that was refused, naming which root.</summary>
    public static string RootSentence(RootRefusal refusal, BackupRoot root)
    {
        var name = root switch
        {
            BackupRoot.Library => "library root",
            BackupRoot.Downloads => "download directory",
            _ => "root",
        };

        return refusal switch
        {
            RootRefusal.Unanswered =>
                $"Enter the {name} path inside this container to restore the paths recorded in the backup.",

            RootRefusal.NotAbsolute =>
                $"Enter an absolute path for the {name} inside this container.",

            RootRefusal.Missing =>
                $"The {name} does not exist inside this container. Check the volume mount and its container path.",

            RootRefusal.NotReadable =>
                $"The {name} exists but is not readable. Grant read access to the container user configured by "
                + $"PUID and PGID.",

            RootRefusal.NotWritable =>
                $"The {name} exists but is not writable. Grant write access to the container user configured "
                + $"by PUID and PGID.",

            RootRefusal.TheSameAsTheOther =>
                "The Library and download directory must be separate directories. Choose a different path for "
                + "one of them.",

            RootRefusal.OverlapsTheOther =>
                "The Library and download directory must not contain one another. Choose separate directories.",

            _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
        };
    }

    /// <summary>
    /// ADR 0043: every sentence a person reads is a value returned from here,
    /// so that a test can read them and hold that no two say the same thing.
    /// </summary>
    /// <remarks>
    /// Four of these are completed by the caller with what was actually found —
    /// the version, the list, the refusal. The sentence is the part that is the
    /// same every time; the detail beside it is the part that is not.
    /// </remarks>
    public static string Sentence(RestoreOutcome outcome) => outcome switch
    {
        RestoreOutcome.Restored =>
            "Backup restored. Sign in with the password saved in the backup. Previous sessions were not restored.",

        RestoreOutcome.NotOffered =>
            "This installation already has a password. Restore requires a fresh, empty installation "
            + "before a password is set.",

        RestoreOutcome.NotABackup =>
            "This file is not a supported fab backup. Select the JSON file created by Export backup.",

        RestoreOutcome.FromANewerTool =>
            "This backup was created by a newer version of fab. Upgrade to that version or later before "
            + "restoring it.",

        RestoreOutcome.RootsNeeded =>
            "Backup checked. Confirm where the Library and completed Downloads are mounted inside this container.",

        RestoreOutcome.NotEmpty =>
            "Restore requires an empty installation. Existing data was found; nothing was changed. Use a "
            + "fresh container to restore this backup.",

        RestoreOutcome.LibraryRootRefused =>
            "The Library root cannot be used. Correct the path or permissions listed below; nothing was restored.",

        RestoreOutcome.DownloadDirectoryRefused =>
            "The download directory cannot be used. Correct the path or permissions listed below; nothing "
            + "was restored.",

        RestoreOutcome.PathOutsideItsRoot =>
            "The backup contains a path outside its recorded root, or a required root was not supplied. "
            + "Check the selected roots and use an unmodified export. Nothing was restored.",

        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    /// <summary>
    /// Every <see cref="BackupPath"/> in the document, with the section it came
    /// from.
    /// </summary>
    /// <remarks>
    /// Found rather than listed. A list would be a second place to keep in step
    /// with the DTOs, and the thing it would fall out of step about — a path
    /// nobody re-rooted — is the one the re-rooting refusal exists to catch. The cost
    /// is one reflection walk per Restore, which happens once in the life of an
    /// installation.
    /// </remarks>
    public static IEnumerable<(string Where, BackupPath Path)> Paths(BackupDocument document)
    {
        foreach (var section in typeof(BackupDocument).GetProperties())
        {
            var value = section.GetValue(document);

            if (value is null) continue;

            if (value is IEnumerable rows and not string)
            {
                foreach (var row in rows)
                {
                    foreach (var path in PathsOf(row))
                    {
                        yield return ($"{section.Name}.{path.Name}", path.Path);
                    }
                }

                continue;
            }

            foreach (var path in PathsOf(value))
            {
                yield return ($"{section.Name}.{path.Name}", path.Path);
            }
        }
    }

    /// <summary>Which roots the document actually uses.</summary>
    /// <remarks>
    /// What makes the Download Directory a question only for the installations
    /// that need one: ADR 0010 lets SABnzbd be skipped, and an installation that
    /// skipped it has nothing under that root to place.
    /// </remarks>
    public static IReadOnlySet<BackupRoot> RootsUsed(BackupDocument document) =>
        Paths(document).Select(found => found.Path.Root).ToHashSet();

    private static IEnumerable<(string Name, BackupPath Path)> PathsOf(object row)
    {
        foreach (var property in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(BackupPath)) continue;

            if (property.GetValue(row) is BackupPath path)
            {
                yield return (property.Name, path);
            }
        }
    }
}
