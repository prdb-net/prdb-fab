namespace Prdb.Fab.Core.Access;

/// <summary>
/// What came of setting the initial password — ADR 0010's first
/// unauthenticated write, and in this slice its only one.
/// </summary>
public enum SetPasswordOutcome
{
    Set,

    /// <summary>
    /// The window is shut. ADR 0010 gates its two unauthenticated writes on one
    /// condition — no password exists yet — and setting one closes it for good.
    /// </summary>
    AlreadySet,

    /// <summary>The password itself was not acceptable. See <see cref="PasswordRule"/>.</summary>
    Refused,
}
