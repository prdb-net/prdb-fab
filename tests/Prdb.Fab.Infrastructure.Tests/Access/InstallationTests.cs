using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Access;

/// <summary>
/// ADR 0033's <c>Installation</c> is one row. The schema says so, rather than
/// the code remembering to.
/// </summary>
public sealed class InstallationTests
{
    [Fact]
    public async Task The_row_is_there_as_soon_as_the_database_is()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();

        var installation = await scope.ServiceProvider
            .GetRequiredService<Installations>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InstallationRow.TheOnlyRow, installation.Id);
        Assert.Null(installation.PasswordHash);
        Assert.Equal(OnboardingStep.Password, installation.OnboardingStep);
    }

    [Fact]
    public async Task A_second_row_is_refused_by_the_database_itself()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Installation.Add(new InstallationRow { Id = 2 });

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.IsType<SqliteException>(failure.InnerException);
    }

    [Fact]
    public async Task Without_a_password_the_next_step_is_the_password()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();

        var installations = scope.ServiceProvider.GetRequiredService<Installations>();

        Assert.True(await installations.IsUnclaimedAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            OnboardingStep.Password,
            await installations.NextStepAsync(TestContext.Current.CancellationToken));
    }
}
