namespace Prdb.Fab.Core.Access;

/// <summary>
/// What came of changing the password. ADR 0020 puts the act on the Account
/// route; ADR 0040 has all three of these cross the contract in a 200 body and
/// by their name.
/// </summary>
public enum ChangePasswordOutcome
{
    /// <summary>
    /// The password is the new one, and every other session has ended —
    /// ADR 0010 makes that the point of the act rather than a side effect.
    /// </summary>
    Changed,

    /// <summary>
    /// The current password was wrong, so nothing changed. Asked for because a
    /// session left open on a borrowed machine must not be a way to lock its
    /// owner out.
    /// </summary>
    WrongPassword,

    /// <summary>
    /// Too many password guesses were made recently, so this attempt was not
    /// checked. The caller may try again when the shared window ends.
    /// </summary>
    TooManyAttempts,

    /// <summary>The new password itself was not acceptable. See <see cref="PasswordRule"/>.</summary>
    Refused,
}
