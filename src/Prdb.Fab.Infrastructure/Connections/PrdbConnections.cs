using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0010's mandatory step: the prdb API key, checked against prdb and then
/// stored — and never one without the other.
/// </summary>
public sealed class PrdbConnections(
    FabDbContext context,
    PrdbGateway prdb,
    ILogger<PrdbConnections> logger)
{
    /// <summary>
    /// Checks the key and stores it. Nothing is written past a failure:
    /// ADR 0010 rejected <em>continue anyway</em> because it only moves the
    /// discovery to a point where it reads as something else entirely.
    /// </summary>
    /// <param name="confirmedAnotherAccount">
    /// Whether the user has been shown, and has accepted, that this key belongs
    /// to a different prdb account than the one this installation was running
    /// as. ADR 0040: the backend computes what a confirmation covers, and this
    /// is the act carrying the answer back.
    /// </param>
    public async Task<PrdbSave> SaveAsync(
        string? apiKey,
        bool confirmedAnotherAccount,
        CancellationToken cancellationToken = default)
    {
        var check = await prdb.CheckAsync(apiKey, cancellationToken);

        if (check.Outcome is not PrdbConnectionOutcome.Saved || check.UserHash is not { } userHash)
        {
            return new PrdbSave(check.Outcome, check.RetryAfterSeconds);
        }

        var installation = await context.Installation.SingleAsync(cancellationToken);

        // ADR 0010: this does not block, because people do move accounts. What
        // it demands is that the consequence is named before it happens rather
        // than discovered afterwards as a wanted list that emptied itself.
        if (installation.PrdbUserHash is { Length: > 0 } previous
            && !string.Equals(previous, userHash, StringComparison.Ordinal)
            && !confirmedAnotherAccount)
        {
            return new PrdbSave(PrdbConnectionOutcome.AnotherAccount, null);
        }

        var changedAccount = installation.PrdbUserHash is { Length: > 0 } held
            && !string.Equals(held, userHash, StringComparison.Ordinal);

        installation.PrdbApiKey = apiKey!.Trim();
        installation.PrdbUserHash = userHash;

        context.Installation.Update(installation);
        await context.SaveChangesAsync(cancellationToken);

        if (changedAccount)
        {
            // ADR 0019 keeps what was already reported and scopes it to the
            // account it was made under, so nothing is deleted here. Worth a
            // line in the log all the same: it is the moment the wanted list
            // stops being the one somebody was looking at.
            logger.LogWarning(
                "The prdb key now in use belongs to a different prdb account than the one this "
                + "installation was running as. What was already reported stays recorded against "
                + "the account it was reported for.");
        }

        logger.LogInformation("The prdb key has been stored.");

        return new PrdbSave(PrdbConnectionOutcome.Saved, null);
    }
}

/// <summary>What happened to the prdb key that was submitted.</summary>
public sealed record PrdbSave(PrdbConnectionOutcome Outcome, int? RetryAfterSeconds);
