namespace Prdb.Fab.Core.Filing;

/// <summary>
/// What ADR 0062's evidence made of one Arriving File, and the sentence the
/// Review Queue shows when it made nothing.
/// </summary>
/// <remarks>
/// A named outcome rather than a boolean, because <em>there was no evidence</em>
/// and <em>the evidence disagreed with itself</em> are different things to the
/// person deciding: the first says nobody has published a picture of this file,
/// and the second says two people have and prdb links them to different Videos.
/// A queue entry that only said <em>not identified</em> would leave them to work
/// out which.
/// </remarks>
public enum PreviewHashOutcome
{
    /// <summary>
    /// No linked, currently visible user preview carries this file's hash — or
    /// the file has no stored hash to look with. The ordinary answer.
    /// </summary>
    NoEvidence,

    /// <summary>
    /// Previews carrying this hash name more than one Video. ADR 0062 assigns
    /// nothing: prdb's own data disagrees with itself about this file, and a
    /// tool that picked one would be guessing.
    /// </summary>
    Conflicting,

    /// <summary>
    /// prdb called the file ambiguous and the Video the evidence names is not
    /// among the candidates it listed. The two disagree, and prdb's list is the
    /// one that stands.
    /// </summary>
    OutsideTheCandidates,

    /// <summary>
    /// The evidence names one Video, and it is not in the Catalogue with enough
    /// detail to file against yet. The assignment waits rather than being made
    /// and then failing at the move — the Catalogue row is pinned and the
    /// repair pass reads it first, so the next run makes it.
    /// </summary>
    WaitingForTheCatalogue,

    /// <summary>The Video was named from the evidence.</summary>
    Assigned,

    /// <summary>
    /// prdb named a Video, so the evidence was not consulted. The authority
    /// answered (ADR 0062).
    /// </summary>
    NotNeeded,
}
