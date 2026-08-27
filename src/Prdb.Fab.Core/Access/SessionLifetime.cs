namespace Prdb.Fab.Core.Access;

/// <summary>
/// How long a session lasts and when its row is written again. ADR 0010: thirty
/// days, extended on use.
/// </summary>
/// <remarks>
/// <em>Extended on use</em> read literally is a write on every request, which
/// would put the schedule's single SQLite writer behind the browser polling a
/// page. So the extension is rationed: a session is written again only once it
/// has aged past <see cref="ExtendAfter"/>, which costs one write a day and
/// still means a session in daily use never expires. The user-visible promise
/// is unchanged, because the only way to notice the difference would be to stop
/// using the tool for thirty days.
/// </remarks>
public static class SessionLifetime
{
    public static readonly TimeSpan Duration = TimeSpan.FromDays(30);

    private static readonly TimeSpan ExtendAfter = TimeSpan.FromDays(1);

    public static DateTimeOffset ExpiresAt(DateTimeOffset now) => now + Duration;

    /// <summary>
    /// Whether a session presented now is still usable. Expiry is a property of
    /// the row rather than of the cookie, which is what makes revoking one take
    /// effect at once.
    /// </summary>
    public static bool IsUsable(DateTimeOffset expiresAt, DateTimeOffset now) => expiresAt > now;

    /// <summary>
    /// Whether this use is worth a write. See the remarks above: true at most
    /// once a day per session.
    /// </summary>
    public static bool ShouldExtend(DateTimeOffset expiresAt, DateTimeOffset now) =>
        IsUsable(expiresAt, now) && expiresAt - now < Duration - ExtendAfter;
}
