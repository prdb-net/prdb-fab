using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// ADR 0039: the four settings, on every connection.
/// </summary>
public sealed class SqlitePragmaTests
{
    [Fact]
    public async Task Every_connection_the_context_opens_carries_the_pragmas()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Twice, through two separate scopes, because the point of ADR 0039 is
        // what the *pool* hands back: the second context may well be given the
        // physical connection the first one finished with.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

            Assert.Equal("wal", await ScalarAsync(context, "PRAGMA journal_mode;"));
            Assert.Equal(1L, await ScalarAsync(context, "PRAGMA synchronous;"));
            Assert.Equal(5000L, await ScalarAsync(context, "PRAGMA busy_timeout;"));
            Assert.Equal(1L, await ScalarAsync(context, "PRAGMA foreign_keys;"));

            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The pragmas are genuinely changes rather than the defaults happening to
    /// agree: a connection to a file nothing has touched carries SQLite's own
    /// answers, and none of them is what ADR 0039 asked for.
    /// </summary>
    [Fact]
    public async Task A_connection_to_an_untouched_file_carries_sqlites_defaults()
    {
        var file = Path.Combine(Path.GetTempPath(), $"prdb-fab-{Guid.NewGuid():n}.db");
        var reachedBy = $"Data Source={file}";

        try
        {
            await using var connection = new SqliteConnection(reachedBy);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            Assert.Equal("delete", await ScalarAsync(connection, "PRAGMA journal_mode;"));
            Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA synchronous;"));
            Assert.Equal(0L, await ScalarAsync(connection, "PRAGMA busy_timeout;"));
        }
        finally
        {
            // This file's pool, not the process's. The test below is about what
            // a pool hands back, and these two run at the same time.
            SqliteConnection.ClearPool(new SqliteConnection(reachedBy));
            File.Delete(file);
        }
    }

    /// <summary>
    /// ADR 0039's actual argument, made mechanical: a connection is pooled by
    /// its connection string and comes back in whatever state the last user
    /// left it in.
    /// </summary>
    /// <remarks>
    /// This is why "set the pragmas once at startup" is a sentence about a
    /// connection nobody can point at afterwards. It cuts both ways — here the
    /// pool happens to return one the interceptor had already configured, and
    /// the next request could as easily be given a brand new one that carries
    /// nothing. Neither is predictable, which is the whole reason the
    /// interceptor sets them on every open rather than trusting either.
    /// </remarks>
    [Fact]
    public async Task A_pooled_connection_comes_back_as_it_was_left()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            await context.Database.CloseConnectionAsync();
        }

        // The same connection string, so the same pool. Nothing here sets a
        // single pragma, and the connection arrives carrying them anyway.
        await using var borrowed = new SqliteConnection(database.Location.ConnectionString);
        await borrowed.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5000L, await ScalarAsync(borrowed, "PRAGMA busy_timeout;"));
    }

    private static Task<object?> ScalarAsync(FabDbContext context, string sql) =>
        ScalarAsync(context.Database.GetDbConnection(), sql);

    private static async Task<object?> ScalarAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return value is string text ? text : Convert.ToInt64(value);
    }
}
