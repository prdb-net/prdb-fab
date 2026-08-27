using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Access;

/// <summary>
/// ADR 0010's session rows: what makes a sign-in survive a restart, and what
/// makes it revocable.
/// </summary>
public sealed class Sessions(FabDbContext context, TimeProvider time)
{
    /// <summary>
    /// 256 bits, which is what makes storing only the hash of a token sound —
    /// there is nothing to guess and nothing to run a dictionary against.
    /// </summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// A new session, and the token the cookie carries. The token is returned
    /// once and never stored, so nothing can hand it back out afterwards.
    /// </summary>
    public async Task<(string Token, DateTimeOffset ExpiresAt)> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();

        // Sweeping here rather than in a routine of its own. ADR 0014 makes
        // everything the tool does on its own a routine, and this is not that:
        // it is one delete on the only path that creates the rows, over a table
        // nobody reads except by token.
        await context.Sessions
            .Where(row => row.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = SessionLifetime.ExpiresAt(now);

        context.Sessions.Add(new SessionRow
        {
            TokenHash = HashOf(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });

        await context.SaveChangesAsync(cancellationToken);

        return (token, expiresAt);
    }

    /// <summary>
    /// The session this token belongs to, or null when there is none, it has
    /// expired, or it was revoked. Extends it where that is worth a write.
    /// </summary>
    public async Task<SessionRow?> AuthenticateAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = HashOf(token);

        var session = await context.Sessions
            .SingleOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var now = time.GetUtcNow();

        if (!SessionLifetime.IsUsable(session.ExpiresAt, now))
        {
            return null;
        }

        if (SessionLifetime.ShouldExtend(session.ExpiresAt, now))
        {
            session.ExpiresAt = SessionLifetime.ExpiresAt(now);
            context.Sessions.Update(session);
            await context.SaveChangesAsync(cancellationToken);
        }

        return session;
    }

    /// <summary>Signing out: the row goes, and the cookie it backed is worthless.</summary>
    public async Task RevokeAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        var hash = HashOf(token);

        await context.Sessions
            .Where(row => row.TokenHash == hash)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// ADR 0010: changing the password <em>ends every other session</em>, which
    /// is the only lever someone has who suspects a session they did not open.
    /// The account form of ticket 10 is what calls this; the operation is here
    /// because it is what a session is for.
    /// </summary>
    public Task<int> RevokeAllExceptAsync(long sessionId, CancellationToken cancellationToken = default) =>
        context.Sessions
            .Where(row => row.Id != sessionId)
            .ExecuteDeleteAsync(cancellationToken);

    private static string HashOf(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
