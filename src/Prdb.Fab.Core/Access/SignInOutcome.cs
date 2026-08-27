namespace Prdb.Fab.Core.Access;

/// <summary>
/// What came of an attempt to sign in. ADR 0040: a verdict is a success, so
/// this crosses the contract in a 200 body and by its name — a wrong password is
/// something the tool checked and can answer, not a broken request.
/// </summary>
public enum SignInOutcome
{
    SignedIn,

    /// <summary>
    /// The only wrong answer there is. Deliberately one outcome and not two:
    /// there is no username, so there is nothing else to have got wrong.
    /// </summary>
    WrongPassword,

    /// <summary>The throttle of ADR 0010, with the wait carried beside it.</summary>
    TooManyAttempts,

    /// <summary>
    /// No password has been set, so there is nothing to sign in to. The caller
    /// is looking at an installation that is still in front of ADR 0010's
    /// window.
    /// </summary>
    NoPasswordYet,
}
