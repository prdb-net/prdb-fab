namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0010's revocable row behind the cookie. Not exported — that ADR says so,
/// and a restored installation ends on the sign-in screen.
/// </summary>
public sealed class SessionRow
{
    /// <summary>An integer surrogate, because this table is not exported (ADR 0033).</summary>
    public long Id { get; set; }

    /// <summary>
    /// SHA-256 of the token the cookie carries, never the token.
    /// </summary>
    /// <remarks>
    /// Not a contradiction of ADR 0037, which is about credentials that have to
    /// be sent back out to a service and therefore cannot be hashed at all. A
    /// session token is minted here from 256 bits of randomness and only ever
    /// compared, so the plain hash is enough — there is no dictionary to run
    /// against it, and no key to keep anywhere. It costs one hash per request
    /// and means a copied database holds no usable session.
    /// </remarks>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thirty days, extended on use (ADR 0010, and <c>SessionLifetime</c> for
    /// how often that is actually written). Expiry lives on the row rather than
    /// in the cookie, which is what makes revoking one take effect at once.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
