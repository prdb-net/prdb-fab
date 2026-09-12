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
                $"This Backup has paths recorded under its {name}, so one has to be given "
                + "before they can be placed.",

            RootRefusal.NotAbsolute =>
                $"The {name} is an absolute path — the one inside this container, which is "
                + "whatever you mounted it at.",

            RootRefusal.Missing =>
                $"There is no such directory in this container. The {name} is a path inside "
                + "a volume you mounted, not the path on the host.",

            RootRefusal.NotReadable =>
                $"That directory is there and this container cannot read it. The {name} has "
                + "to be readable by the PUID and PGID this container runs as.",

            RootRefusal.NotWritable =>
                $"That directory is there and this container cannot write to it. The {name} "
                + "has to be owned by the PUID and PGID this container runs as.",

            RootRefusal.TheSameAsTheOther =>
                "The library and the downloads cannot be the same directory. Filing moves "
                + "videos out of one and into the other, and a tool that sorts within the "
                + "directory it also watches ends up re-processing its own output.",

            RootRefusal.OverlapsTheOther =>
                "One of these two directories is inside the other. Filing moves videos out "
                + "of the downloads and into the library, and nesting them means the library "
                + "is watched as a download directory or the other way round.",

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
            "This installation is now the one in the file. Sign in with the password it "
            + "carried — sessions are not in a Backup, so the one you had is not either.",

        RestoreOutcome.NotOffered =>
            "This installation already has a password, so restoring into it is no longer "
            + "offered. A Restore replaces an installation rather than joining one, and it "
            + "runs on a container that has nothing in it yet.",

        RestoreOutcome.NotABackup =>
            "That file is not a Backup this tool can read. A Backup is the JSON document the "
            + "export produces, and it says its own format version in the first few lines.",

        RestoreOutcome.FromANewerTool =>
            "That Backup was written by a newer version of this tool than the one running "
            + "here. Reading the parts of it this version recognises would quietly drop the "
            + "rest, so run the version that wrote it instead.",

        RestoreOutcome.RootsNeeded =>
            "The Backup is readable and this installation is empty. What it cannot know is "
            + "where its library and its downloads are mounted in this container — the paths "
            + "inside it are recorded against those two roots, and they are answered once, "
            + "here.",

        RestoreOutcome.NotEmpty =>
            "This installation already holds something, so nothing was changed. A Restore "
            + "never merges: it is for a container that has nothing yet.",

        RestoreOutcome.LibraryRootRefused =>
            "The library root cannot be used, so nothing was restored.",

        RestoreOutcome.DownloadDirectoryRefused =>
            "The download directory cannot be used, so nothing was restored. The Backup has "
            + "paths under it, which is why it is being asked for.",

        RestoreOutcome.PathOutsideItsRoot =>
            "A path in the Backup does not sit under the root it names, so nothing was "
            + "restored. That is either a root this Backup needs and nobody answered, or a "
            + "file that has been edited — and re-rooting must not be able to place a path "
            + "somewhere it was not pointed at.",

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
