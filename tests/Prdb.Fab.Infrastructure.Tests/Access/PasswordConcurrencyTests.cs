using System.Data.Common;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Access;

public sealed class PasswordConcurrencyTests
{
    private const string Password = "the original long password";
    private const string Replacement = "the replacement long password";

    [Fact]
    public async Task A_second_initial_password_cannot_overwrite_the_first_claim()
    {
        var interleave = new AfterInstallationRead();
        await using var database = await CreateAsync(interleave);
        interleave.Next = async () =>
        {
            await using var winner = database.Scope();
            Assert.Equal(SetPasswordOutcome.Set, (await winner.ServiceProvider
                .GetRequiredService<PasswordGate>().SetInitialAsync(Password, TestContext.Current.CancellationToken)).Outcome);
        };

        await using var scope = database.Scope();
        var verdict = await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .SetInitialAsync(Replacement, TestContext.Current.CancellationToken);

        Assert.Equal(SetPasswordOutcome.AlreadySet, verdict.Outcome);
        await AssertPasswordAsync(database, Password, Replacement);
    }

    [Fact]
    public async Task Changing_a_password_preserves_settings_saved_after_it_was_read()
    {
        var interleave = new AfterInstallationRead();
        await using var database = await CreateAsync(interleave);
        await SetAsync(database);
        interleave.Next = async () =>
        {
            await using var writer = database.Scope();
            await writer.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.LibraryRoot, "/new-library"),
                TestContext.Current.CancellationToken);
        };

        await using var scope = database.Scope();
        var verdict = await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .ChangeAsync(Password, Replacement, 0, TestContext.Current.CancellationToken);

        Assert.Equal(ChangePasswordOutcome.Changed, verdict.Outcome);
        await using var check = database.Scope();
        Assert.Equal("/new-library", (await check.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken)).LibraryRoot);
    }

    [Fact]
    public async Task A_password_change_cannot_replace_a_newer_password_using_the_old_one()
    {
        var interleave = new AfterInstallationRead();
        await using var database = await CreateAsync(interleave);
        await SetAsync(database);
        interleave.Next = async () =>
        {
            await using var winner = database.Scope();
            Assert.Equal(ChangePasswordOutcome.Changed, (await winner.ServiceProvider
                .GetRequiredService<PasswordGate>().ChangeAsync(Password, Replacement, 0,
                    TestContext.Current.CancellationToken)).Outcome);
        };

        await using var scope = database.Scope();
        var verdict = await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .ChangeAsync(Password, "a competing long password", 0, TestContext.Current.CancellationToken);

        Assert.Equal(ChangePasswordOutcome.WrongPassword, verdict.Outcome);
        await AssertPasswordAsync(database, Replacement, "a competing long password");
    }

    [Fact]
    public async Task Rehashing_a_sign_in_cannot_restore_a_password_that_was_changed()
    {
        var interleave = new AfterInstallationRead();
        await using var database = await CreateAsync(interleave);
        await using (var seed = database.Scope())
        {
            var hasher = new PasswordHasher<InstallationRow>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
            }));
            var hash = hasher.HashPassword(new InstallationRow(), Password);
            await seed.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PasswordHash, hash), TestContext.Current.CancellationToken);
        }

        interleave.Next = async () =>
        {
            await using var winner = database.Scope();
            Assert.Equal(ChangePasswordOutcome.Changed, (await winner.ServiceProvider
                .GetRequiredService<PasswordGate>().ChangeAsync(Password, Replacement, 0,
                    TestContext.Current.CancellationToken)).Outcome);
        };

        await using var scope = database.Scope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .VerifyAsync(Password, TestContext.Current.CancellationToken));
        await AssertPasswordAsync(database, Replacement, Password);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_session_revocation_rolls_back_the_password_write(bool reset)
    {
        await using var database = await TestDatabase.CreateAsync();
        await SetAsync(database);
        string token;
        await using (var seed = database.Scope())
        {
            (token, _) = await seed.ServiceProvider.GetRequiredService<Sessions>()
                .CreateAsync(TestContext.Current.CancellationToken);
            await seed.ServiceProvider.GetRequiredService<FabDbContext>().Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER refuse_session_deletion BEFORE DELETE ON session
                BEGIN SELECT RAISE(ABORT, 'session deletion unavailable'); END;
                """, TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var passwords = scope.ServiceProvider.GetRequiredService<PasswordGate>();
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
            {
                if (reset)
                {
                    await passwords.ClearAsync(TestContext.Current.CancellationToken);
                }
                else
                {
                    await passwords.ChangeAsync(Password, Replacement, 0, TestContext.Current.CancellationToken);
                }
            });
        }

        await AssertPasswordAsync(database, Password, Replacement);
        await using var check = database.Scope();
        Assert.NotNull(await check.ServiceProvider.GetRequiredService<Sessions>()
            .AuthenticateAsync(token, TestContext.Current.CancellationToken));
    }

    private static Task<TestDatabase> CreateAsync(AfterInstallationRead interleave) =>
        TestDatabase.CreateAsync(also: services => services.AddDbContext<FabDbContext>(
            options => options.AddInterceptors(interleave)));

    private static async Task SetAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<PasswordGate>()
            .SetInitialAsync(Password, TestContext.Current.CancellationToken);
    }

    private static async Task AssertPasswordAsync(TestDatabase database, string accepted, string refused)
    {
        await using var scope = database.Scope();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordGate>();
        Assert.True(await passwords.VerifyAsync(accepted, TestContext.Current.CancellationToken));
        Assert.False(await passwords.VerifyAsync(refused, TestContext.Current.CancellationToken));
    }

    // Both callers use real SQLite and the real services. Interleave a second
    // request after the first obtained its snapshot, without timing sleeps.
    private sealed class AfterInstallationRead : DbCommandInterceptor
    {
        public Func<Task>? Next { get; set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Next is { } next && command.CommandText.Contains("FROM \"installation\"", StringComparison.Ordinal))
            {
                Next = null;
                await next();
            }

            return result;
        }
    }
}
