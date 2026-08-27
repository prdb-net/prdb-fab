namespace Prdb.Fab.Core.Access;

/// <summary>
/// Where onboarding has got to. ADR 0010's path, as the marker the
/// <c>Installation</c> row carries: <em>which step is next</em> is the state, so
/// a closed tab costs nothing and each step commits on its own.
/// </summary>
/// <remarks>
/// The first choice ADR 0010 names — fresh, or restore a backup — is not a step
/// here. It is a fork in front of the path rather than a thing to be completed,
/// and restore is not built in this slice at all.
/// </remarks>
public enum OnboardingStep
{
    /// <summary>
    /// No password has been set. The one condition ADR 0010 gates its two
    /// unauthenticated writes on, and the state <c>FAB_RESET_PASSWORD</c>
    /// returns an installation to.
    /// </summary>
    Password,

    /// <summary>Mandatory: without it there is no identification, no wanted list, no artwork.</summary>
    PrdbKey,

    /// <summary>Skippable. A tool that cannot download is still a tool that holds a library.</summary>
    Sabnzbd,

    /// <summary>Skippable; one indexer is enough when it is taken.</summary>
    Indexers,

    /// <summary>Mandatory: it is where filing puts things.</summary>
    LibraryRoot,

    /// <summary>
    /// The path is finished and does not return. What a skipped step left
    /// behind is a Gap, carried on the connection rather than here.
    /// </summary>
    Complete,
}
