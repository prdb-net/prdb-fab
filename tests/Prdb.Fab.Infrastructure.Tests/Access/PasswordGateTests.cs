using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Access;

/// <summary>
/// ADR 0010's window: two unauthenticated writes gated on one condition, and
/// setting the password closes it for good.
/// </summary>
public sealed class PasswordGateTests
{
    private const string Password = "a long enough password";

    [Fact]
    public async Task The_password_can_be_set_once()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.Equal(SetPasswordOutcome.Set, await SetAsync(database, Password));

        // The window is shut. It stays shut for the second unauthenticated
        // write too, which is why the condition is asked of the installation
        // rather than written into a caller.
        Assert.Equal(SetPasswordOutcome.AlreadySet, await SetAsync(database, "another long password"));
    }

    [Fact]
    public async Task A_password_the_rule_refuses_is_not_stored()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.Equal(SetPasswordOutcome.Refused, await SetAsync(database, "short"));

        await using var scope = database.Scope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<Installations>()
            .IsUnclaimedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Setting_it_moves_onboarding_on_to_the_prdb_key()
    {
        await using var database = await TestDatabase.CreateAsync();

        await SetAsync(database, Password);

        await using var scope = database.Scope();
        Assert.Equal(
            OnboardingStep.PrdbKey,
            await scope.ServiceProvider.GetRequiredService<Installations>()
                .NextStepAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Only_the_password_verifies()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SetAsync(database, Password);

        Assert.True(await VerifyAsync(database, Password));
        Assert.False(await VerifyAsync(database, "not the password"));
        Assert.False(await VerifyAsync(database, string.Empty));
    }

    /// <summary>
    /// Before a password exists there is nothing to be wrong about, which the
    /// sign-in verdict says as its own outcome rather than as a wrong password.
    /// </summary>
    [Fact]
    public async Task Before_a_password_exists_verifying_answers_neither_way()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.Null(await VerifyAsync(database, Password));
    }

    /// <summary>
    /// ADR 0010's password change: it requires the current password and ends
    /// every other session, which is the only lever somebody has who suspects
    /// a session they did not open.
    /// </summary>
    [Fact]
    public async Task Changing_it_ends_every_session_but_the_one_that_asked()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SetAsync(database, Password);

        var mine = await SignInAsync(database);
        var elsewhere = await SignInAsync(database);
        var third = await SignInAsync(database);

        var (outcome, refusal, ended) = await ChangeAsync(database, Password, "a new long password", mine.Id);

        Assert.Equal(ChangePasswordOutcome.Changed, outcome);
        Assert.Null(refusal);
        Assert.Equal(2, ended);

        // The one that asked still works, and the other two are gone from the
        // moment of the change rather than at their expiry.
        Assert.NotNull(await AuthenticateAsync(database, mine.Token));
        Assert.Null(await AuthenticateAsync(database, elsewhere.Token));
        Assert.Null(await AuthenticateAsync(database, third.Token));

        Assert.True(await VerifyAsync(database, "a new long password"));
        Assert.False(await VerifyAsync(database, Password));
    }

    /// <summary>
    /// A session left open on a borrowed machine must not be a way to lock the
    /// owner out of their own installation.
    /// </summary>
    [Fact]
    public async Task The_current_password_is_required_and_a_wrong_one_changes_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SetAsync(database, Password);

        var mine = await SignInAsync(database);
        var elsewhere = await SignInAsync(database);

        var (outcome, _, ended) = await ChangeAsync(database, "not the password", "a new long password", mine.Id);

        Assert.Equal(ChangePasswordOutcome.WrongPassword, outcome);
        Assert.Equal(0, ended);

        Assert.True(await VerifyAsync(database, Password));
        Assert.NotNull(await AuthenticateAsync(database, elsewhere.Token));
    }

    /// <summary>
    /// The same rule as the first one, and the same reason: a change is where a
    /// short password would otherwise get in behind the rule that refused it.
    /// </summary>
    [Fact]
    public async Task A_new_password_the_rule_refuses_changes_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SetAsync(database, Password);

        var mine = await SignInAsync(database);
        var elsewhere = await SignInAsync(database);

        var (outcome, refusal, ended) = await ChangeAsync(database, Password, "short", mine.Id);

        Assert.Equal(ChangePasswordOutcome.Refused, outcome);
        Assert.NotNull(refusal);
        Assert.Equal(0, ended);

        Assert.True(await VerifyAsync(database, Password));
        Assert.NotNull(await AuthenticateAsync(database, elsewhere.Token));
    }

    /// <summary>
    /// ADR 0010's recovery path, and ADR 0037's reason for refusing to derive an
    /// encryption key from the password: the reset must not be the destruction
    /// path for every other credential.
    /// </summary>
    [Fact]
    public async Task Clearing_the_password_leaves_every_other_credential_standing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SetAsync(database, Password);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Persistence.FabDbContext>();
            var installation = await scope.ServiceProvider.GetRequiredService<Installations>()
                .ReadAsync(TestContext.Current.CancellationToken);

            installation.PrdbApiKey = "a key";
            installation.LibraryRoot = "/library";
            installation.OnboardingStep = OnboardingStep.Complete;

            context.Installation.Update(installation);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<PasswordGate>()
                .ClearAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var installations = scope.ServiceProvider.GetRequiredService<Installations>();
            var installation = await installations.ReadAsync(TestContext.Current.CancellationToken);

            Assert.Null(installation.PasswordHash);
            Assert.Equal("a key", installation.PrdbApiKey);
            Assert.Equal("/library", installation.LibraryRoot);

            // What the user is asked for is a password and nothing else: the
            // marker is left where it was, so setting one returns them to a
            // finished installation rather than to the start of onboarding.
            Assert.Equal(OnboardingStep.Complete, installation.OnboardingStep);
            Assert.Equal(
                OnboardingStep.Password,
                await installations.NextStepAsync(TestContext.Current.CancellationToken));
        }

        // And the window is open again, which is the whole point of the reset.
        Assert.Equal(SetPasswordOutcome.Set, await SetAsync(database, "a replacement password"));

        await using (var scope = database.Scope())
        {
            Assert.Equal(
                OnboardingStep.Complete,
                await scope.ServiceProvider.GetRequiredService<Installations>()
                    .NextStepAsync(TestContext.Current.CancellationToken));
        }
    }

    private static async Task<SetPasswordOutcome> SetAsync(TestDatabase database, string password)
    {
        await using var scope = database.Scope();

        var (outcome, _) = await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .SetInitialAsync(password, TestContext.Current.CancellationToken);

        return outcome;
    }

    private static async Task<bool?> VerifyAsync(TestDatabase database, string password)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .VerifyAsync(password, TestContext.Current.CancellationToken);
    }

    private static async Task<(long Id, string Token)> SignInAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        var sessions = scope.ServiceProvider.GetRequiredService<Sessions>();
        var (token, _) = await sessions.CreateAsync(TestContext.Current.CancellationToken);
        var session = await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken);

        return (session!.Id, token);
    }

    private static async Task<Persistence.SessionRow?> AuthenticateAsync(TestDatabase database, string token)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<Sessions>()
            .AuthenticateAsync(token, TestContext.Current.CancellationToken);
    }

    private static async Task<(ChangePasswordOutcome Outcome, string? Refusal, int Ended)> ChangeAsync(
        TestDatabase database,
        string current,
        string next,
        long sessionId)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .ChangeAsync(current, next, sessionId, TestContext.Current.CancellationToken);
    }
}
