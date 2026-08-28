namespace Prdb.Fab.Core.Filing;

/// <summary>What one candidate directory on disk looks like to filing.</summary>
public enum DirectoryState
{
    /// <summary>Nothing is at the path.</summary>
    Absent,

    /// <summary>A directory is there and holds nothing.</summary>
    EmptyDirectory,

    /// <summary>A directory is there and holds something.</summary>
    OccupiedDirectory,

    /// <summary>Something that is not a directory is at the path.</summary>
    NotADirectory,

    /// <summary>The path could not be read at all.</summary>
    Unreadable,
}

/// <summary>What filing does with a computed entry directory.</summary>
public enum EntryDirectoryVerdict
{
    /// <summary>Use the computed name.</summary>
    Use,

    /// <summary>Use the computed name with prdb's video id appended.</summary>
    Distinguish,

    /// <summary>File nothing, and say so.</summary>
    Refuse,
}

/// <summary>Where a video already filed under this entry stands now.</summary>
public enum RecordedEntryState
{
    /// <summary>The directory and the recorded file are both there.</summary>
    FileIsThere,

    /// <summary>The directory is there and the recorded file is gone.</summary>
    FileIsGone,

    /// <summary>The directory itself is gone.</summary>
    DirectoryIsGone,
}

/// <summary>What arrives beside, or instead of, a copy already filed.</summary>
public enum SecondQualityVerdict
{
    /// <summary>Label the file already filed, then file the newcomer labelled.</summary>
    RelabelThenFile,

    /// <summary>File the newcomer unlabelled: it is the only copy again.</summary>
    FileUnlabelled,

    /// <summary>File nothing; the entry this video was held in is not there.</summary>
    EntryMissing,
}

/// <summary>
/// The decisions ADR 0017 makes about a computed path, kept apart from the
/// filesystem that answers them.
/// </summary>
/// <remarks>
/// Filing asks these questions with a `stat` in hand and acts on the verdict, so
/// the rule can be argued and tested without a disk, and the routine that owns
/// the disk contains no policy.
/// </remarks>
public static class FiledPaths
{
    /// <summary>
    /// What to do with a computed entry directory, given what is at it and at
    /// the distinguished name beside it.
    /// </summary>
    /// <remarks>
    /// A directory that exists and is empty is free: a filing that stopped half
    /// way, or a directory somebody made, is not another video's. Occupied by
    /// something else, the name is distinguished with prdb's video id — the full
    /// id rather than a prefix, because a collision needs the same site, the same
    /// date and the same title, and when the ugliness is on screen an identifier
    /// that can be looked up is worth more than a shorter name.
    ///
    /// Where the distinguished name is taken too, or where either state could not
    /// be read, nothing is filed. Sidestepping is right for a collision and wrong
    /// for everything else: a permissions or mount problem must not quietly
    /// produce a second library beside the first.
    /// </remarks>
    public static EntryDirectoryVerdict For(DirectoryState computed, DirectoryState distinguished)
    {
        if (computed is DirectoryState.Unreadable || distinguished is DirectoryState.Unreadable)
        {
            return EntryDirectoryVerdict.Refuse;
        }

        if (IsFree(computed))
        {
            return EntryDirectoryVerdict.Use;
        }

        return IsFree(distinguished)
            ? EntryDirectoryVerdict.Distinguish
            : EntryDirectoryVerdict.Refuse;
    }

    /// <summary>
    /// What to do when a second Quality of a video arrives, given where the copy
    /// already filed stands.
    /// </summary>
    /// <remarks>
    /// The first copy of a video is filed unlabelled, because at that point there
    /// is only one of it. When a second Quality arrives the file already filed is
    /// renamed to carry its own label and the newcomer is written beside it, so
    /// the version list reads two labels rather than one full file name beside
    /// one label. The order is fixed — relabel first — so that an interruption
    /// leaves one correctly labelled file, which is a valid entry.
    ///
    /// Where the recorded path no longer resolves, the entry directory decides. A
    /// tidied-up directory takes the newcomer unlabelled, because it is the only
    /// copy again and there is nothing to relabel. A directory that is gone files
    /// nothing: a deliberately deleted entry and a mount that silently did not
    /// come up look identical from one `stat`, and the careful side of that
    /// confusion is the one this product already chose elsewhere.
    /// </remarks>
    public static SecondQualityVerdict ForSecondQuality(RecordedEntryState recorded) =>
        recorded switch
        {
            RecordedEntryState.FileIsThere => SecondQualityVerdict.RelabelThenFile,
            RecordedEntryState.FileIsGone => SecondQualityVerdict.FileUnlabelled,
            _ => SecondQualityVerdict.EntryMissing,
        };

    /// <summary>
    /// The name the arriving file is put under while a replace is in progress.
    /// </summary>
    /// <remarks>
    /// It begins with a dot, which hides it from the media server's scanner and
    /// from this tool's own walk, and it carries a suffix that is not a video
    /// container. The name deliberately does not begin with the entry directory's
    /// own name, so the version grouping rule cannot reach it mid-scan, and
    /// naming the download makes the leftover of an interrupted replace
    /// attributable rather than anonymous.
    /// </remarks>
    public static string TemporaryName(Guid downloadId) => $".filing-{downloadId:d}.part";

    private static bool IsFree(DirectoryState state) =>
        state is DirectoryState.Absent or DirectoryState.EmptyDirectory;
}
