using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Access;

/// <summary>
/// ADR 0010's session rows: what makes a sign-in survive a restart, and what
/// makes it revocable.
/// </summary>
public sealed class SessionsTests
{
    [Fact]
    public async Task A_session_is_found_by_the_token_it_was_created_with()
    {
        await using var database = await TestDatabase.CreateAsync();

        var (token, expiresAt) = await CreateAsync(database);

        Assert.Equal(SessionLifetime.ExpiresAt(database.Time.GetUtcNow()), expiresAt);
        Assert.NotNull(await AuthenticateAsync(database, token));
        Assert.Null(await AuthenticateAsync(database, "not a token"));
        Assert.Null(await AuthenticateAsync(database, token: null));
    }

    /// <summary>
    /// The token is minted here and never stored, so a copied database holds no
    /// usable session.
    /// </summary>
    [Fact]
    public async Task The_token_itself_is_nowhere_in_the_database()
    {
        await using var database = await TestDatabase.CreateAsync();

        var (token, _) = await CreateAsync(database);

        await using var scope = database.Scope();
        var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Sessions.ToListAsync(TestContext.Current.CancellationToken);

        Assert.All(rows, row => Assert.NotEqual(token, row.TokenHash));
    }

    [Fact]
    public async Task A_session_stops_working_when_it_expires()
    {
        await using var database = await TestDatabase.CreateAsync();

        var (token, _) = await CreateAsync(database);

        database.Time.Advance(SessionLifetime.Duration + TimeSpan.FromSeconds(1));

        Assert.Null(await AuthenticateAsync(database, token));
    }

    [Fact]
    public async Task A_session_in_use_is_extended()
    {
        await using var database = await TestDatabase.CreateAsync();

        var (token, _) = await CreateAsync(database);

        // Every day for a fortnight past its original expiry.
        for (var day = 0; day < 44; day++)
        {
            database.Time.Advance(TimeSpan.FromDays(1));
            Assert.NotNull(await AuthenticateAsync(database, token));
        }
    }

    [Fact]
    public async Task Signing_out_takes_effect_at_once()
    {
        await using var database = await TestDatabase.CreateAsync();

        var (token, _) = await CreateAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<Sessions>()
                .RevokeAsync(token, TestContext.Current.CancellationToken);
        }

        Assert.Null(await AuthenticateAsync(database, token));
    }

    /// <summary>
    /// ADR 0010: the only lever someone has who suspects a session they did not
    /// open. The one they are holding survives, or changing the password would
    /// sign them out of the form they changed it in.
    /// </summary>
    [Fact]
    public async Task Ending_every_other_session_keeps_the_one_asking()
    {
        await using var database = await TestDatabase.CreateAsync();

        var (mine, _) = await CreateAsync(database);
        var (theirs, _) = await CreateAsync(database);

        var session = await AuthenticateAsync(database, mine);
        Assert.NotNull(session);

        await using (var scope = database.Scope())
        {
            var ended = await scope.ServiceProvider.GetRequiredService<Sessions>()
                .RevokeAllExceptAsync(session!.Id, TestContext.Current.CancellationToken);

            Assert.Equal(1, ended);
        }

        Assert.NotNull(await AuthenticateAsync(database, mine));
        Assert.Null(await AuthenticateAsync(database, theirs));
    }

    [Fact]
    public async Task Dead_rows_go_when_a_new_session_is_created()
    {
        await using var database = await TestDatabase.CreateAsync();

        await CreateAsync(database);

        database.Time.Advance(SessionLifetime.Duration + TimeSpan.FromSeconds(1));

        await CreateAsync(database);

        await using var scope = database.Scope();
        var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Sessions.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, rows);
    }

    private static async Task<(string Token, DateTimeOffset ExpiresAt)> CreateAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<Sessions>()
            .CreateAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SessionRow?> AuthenticateAsync(TestDatabase database, string? token)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<Sessions>()
            .AuthenticateAsync(token, TestContext.Current.CancellationToken);
    }
}
