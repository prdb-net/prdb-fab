using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Access;

/// <summary>
/// Reads and writes the one <c>Installation</c> row.
/// </summary>
/// <remarks>
/// The row is created by the migration, so everything here can take it for
/// granted rather than carrying a branch nobody ever exercises.
/// </remarks>
public sealed class Installations(FabDbContext context)
{
    public Task<InstallationRow> ReadAsync(CancellationToken cancellationToken = default) =>
        context.Installation.SingleAsync(cancellationToken);

    /// <summary>
    /// Whether ADR 0010's window is open: no password exists yet.
    /// </summary>
    public async Task<bool> IsUnclaimedAsync(CancellationToken cancellationToken = default) =>
        (await ReadAsync(cancellationToken)).PasswordHash is null;

    /// <summary>
    /// Which step the browser side should show. ADR 0010: <em>one anonymous
    /// state endpoint answers whether a password is set, whether this caller is
    /// signed in, and which onboarding step is next.</em>
    /// </summary>
    /// <remarks>
    /// Setting a password is in front of the path rather than the first thing on
    /// it, which is what makes <c>FAB_RESET_PASSWORD</c> honest: it clears the
    /// password on an installation that has finished onboarding, and what the
    /// user is asked for afterwards is a password and nothing else. The stored
    /// marker is left exactly where it was.
    /// </remarks>
    public async Task<OnboardingStep> NextStepAsync(CancellationToken cancellationToken = default)
    {
        var installation = await ReadAsync(cancellationToken);

        return installation.PasswordHash is null ? OnboardingStep.Password : installation.OnboardingStep;
    }
}
