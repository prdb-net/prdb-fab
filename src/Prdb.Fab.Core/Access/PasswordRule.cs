namespace Prdb.Fab.Core.Access;

/// <summary>
/// What is accepted as the single password of ADR 0010.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0010 settled that there is one password, where it lives and how it is
/// hashed, and deliberately said nothing about its shape — that is build
/// mechanism, decided here.
/// </para>
/// <para>
/// A length floor and nothing else. Composition rules — a digit, a symbol, a
/// capital — are what NIST SP 800-63B stopped recommending, because they push
/// people towards a shorter password with a predictable shape and towards
/// writing it down. There is one user, one secret, and a rate limit in front of
/// it (ADR 0010), so length is the property worth insisting on.
/// </para>
/// <para>
/// The ceiling is not a security rule. It stops a request body from being run
/// through a deliberately slow hash, which is the one way a password field
/// becomes a denial of service.
/// </para>
/// <para>
/// Nothing is trimmed. A password is a secret rather than a name, and a space
/// somebody chose is a character of it.
/// </para>
/// </remarks>
public static class PasswordRule
{
    public const int MinimumLength = 8;

    public const int MaximumLength = 256;

    /// <summary>
    /// The reason this password is not acceptable, or null when it is. ADR 0043:
    /// a rule in Core cannot log, so it returns its reason and the caller shows
    /// it.
    /// </summary>
    public static string? Refuse(string? password) => password switch
    {
        null or "" => "A password is needed.",
        { Length: < MinimumLength } => $"A password needs at least {MinimumLength} characters.",
        { Length: > MaximumLength } => $"A password can be at most {MaximumLength} characters.",
        _ => null,
    };
}
