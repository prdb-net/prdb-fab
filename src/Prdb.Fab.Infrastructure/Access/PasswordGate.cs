using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Access;

/// <summary>
/// ADR 0010's single password: setting it, and checking one against it.
/// </summary>
/// <remarks>
/// Hashed with ASP.NET Core's <see cref="PasswordHasher{TUser}"/>, which is
/// versioned and rehashes on sign-in. ADR 0010 deliberately does not reuse the
/// Argon2id that ADR 0009 needs for a backup passphrase: the hash sits in the
/// same database as the prdb key and the indexer keys, which ADR 0037 keeps in
/// the clear, so whoever can read it already holds the secrets worth having.
/// The file that is designed to travel is the one that gets the memory-hard
/// derivation.
/// </remarks>
public sealed class PasswordGate(
    FabDbContext context,
    Installations installations,
    Sessions sessions,
    ILogger<PasswordGate> logger)
{
    private static readonly PasswordHasher<InstallationRow> Hasher = new();

    /// <summary>
    /// The first of ADR 0010's two unauthenticated writes, gated on the one
    /// condition both of them share: no password exists yet.
    /// </summary>
    public async Task<(SetPasswordOutcome Outcome, string? Refusal)> SetInitialAsync(
        string? password,
        CancellationToken cancellationToken = default)
    {
        var refusal = PasswordRule.Refuse(password);
        if (refusal is not null)
        {
            return (SetPasswordOutcome.Refused, refusal);
        }

        var installation = await context.Installation.SingleAsync(cancellationToken);

        if (installation.PasswordHash is not null)
        {
            // Setting one closes the window for good. Nothing here is a race
            // worth guarding beyond this: the row is one row, and the write
            // below is one statement against it.
            return (SetPasswordOutcome.AlreadySet, null);
        }

        installation.PasswordHash = Hasher.HashPassword(installation, password!);

        // The marker stays where it is. A fresh installation is already sitting
        // on OnboardingStep.Password and moves to the next step by being read
        // (see Installations.NextStepAsync); one whose password was cleared by
        // FAB_RESET_PASSWORD returns to wherever it had got to.
        if (installation.OnboardingStep == OnboardingStep.Password)
        {
            installation.OnboardingStep = OnboardingPath.After(OnboardingStep.Password);
        }

        context.Installation.Update(installation);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("The password has been set. Onboarding continues at {Step}.", installation.OnboardingStep);

        return (SetPasswordOutcome.Set, null);
    }

    /// <summary>
    /// ADR 0010's password change: it requires the current password and
    /// <em>ends every other session</em>, which is the only lever someone has
    /// who suspects a session they did not open.
    /// </summary>
    /// <param name="currentSessionId">
    /// The session making the request, which is the one that survives. Signing
    /// the user out of the browser they are changing it in would make the lever
    /// cost more than the thing it is pulled against.
    /// </param>
    /// <remarks>
    /// The current password is asked for rather than taken as proven by the
    /// session: a session left open on a borrowed machine must not be a way to
    /// lock its owner out of their own installation.
    /// </remarks>
    public async Task<(ChangePasswordOutcome Outcome, string? Refusal, int SessionsEnded)> ChangeAsync(
        string? current,
        string? next,
        long currentSessionId,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);
        var verification = Verify(installation, current);

        if (verification is null or PasswordVerificationResult.Failed)
        {
            return (ChangePasswordOutcome.WrongPassword, null, 0);
        }

        var refusal = PasswordRule.Refuse(next);
        if (refusal is not null)
        {
            return (ChangePasswordOutcome.Refused, refusal, 0);
        }

        installation.PasswordHash = Hasher.HashPassword(installation, next!);

        context.Installation.Update(installation);
        await context.SaveChangesAsync(cancellationToken);

        var ended = await sessions.RevokeAllExceptAsync(currentSessionId, cancellationToken);

        logger.LogInformation(
            "The password has been changed, and {Count} other session(s) ended with it.",
            ended);

        return (ChangePasswordOutcome.Changed, null, ended);
    }

    /// <summary>
    /// Whether this is the password, rehashing it when the hasher says the
    /// stored form is out of date. Null when no password has been set at all,
    /// which is a different answer from "wrong".
    /// </summary>
    public async Task<bool?> VerifyAsync(string? password, CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);
        var result = Verify(installation, password);

        if (result is null)
        {
            return null;
        }

        if (result == PasswordVerificationResult.Failed)
        {
            return false;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The whole reason ADR 0010 named this hasher: it is versioned, and
            // the version moves under an installation that has been running for
            // years.
            installation.PasswordHash = Hasher.HashPassword(installation, password!);
            context.Installation.Update(installation);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("The stored password hash has been upgraded to the current format.");
        }

        return true;
    }

    private static PasswordVerificationResult? Verify(InstallationRow installation, string? password)
    {
        if (installation.PasswordHash is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(password))
        {
            return PasswordVerificationResult.Failed;
        }

        return Hasher.VerifyHashedPassword(installation, installation.PasswordHash, password);
    }

    /// <summary>
    /// ADR 0010's recovery path, taken at the host rather than over the network:
    /// clear the password and every session, and leave everything else standing.
    /// </summary>
    /// <remarks>
    /// Every other credential survives, which is the property ADR 0037 leaned on
    /// when it refused to derive an encryption key from this password — the
    /// recovery path would otherwise be the destruction path.
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);

        if (installation.PasswordHash is not null)
        {
            installation.PasswordHash = null;
            context.Installation.Update(installation);
            await context.SaveChangesAsync(cancellationToken);
        }

        await context.Sessions.ExecuteDeleteAsync(cancellationToken);

        logger.LogWarning(
            "FAB_RESET_PASSWORD cleared the password and ended every session. Remove the variable "
            + "and restart, or the next restart will clear the password you are about to set. "
            + "Onboarding continues at {Step}.",
            await installations.NextStepAsync(cancellationToken));
    }
}
