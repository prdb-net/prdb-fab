using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// Brings the database to the schema this build expects, at startup, before the
/// listener and before the lanes (ADR 0039).
/// </summary>
public sealed class DatabaseMigrator(
    FabDbContext context,
    FabDatabaseLocation location,
    ILogger<DatabaseMigrator> logger)
{
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            location.EnsureDirectoryExists();

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (pending.Length > 0)
            {
                logger.LogInformation(
                    "Applying {Count} migration(s) to {Database}: {Migrations}.",
                    pending.Length,
                    location.FilePath,
                    string.Join(", ", pending));
            }

            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // ADR 0044: there is no way back from a migration, so a half-applied
            // one is the state nobody can talk a user out of. Stopping here is
            // ADR 0004's rule and the reason the release notes say to copy /data
            // before updating.
            logger.LogCritical(
                exception,
                "The database at {Database} could not be migrated. The tool stops here rather "
                + "than running against a schema it does not understand.",
                location.FilePath);

            throw new DatabaseMigrationException(
                $"The database at {location.FilePath} could not be migrated.",
                exception);
        }

        await WarnIfNotWriteAheadLoggingAsync(cancellationToken);
        await AnalyseAsync(cancellationToken);
    }

    /// <summary>
    /// ADR 0056: one <c>PRAGMA optimize</c> per start, so the query planner
    /// reads the data rather than its own built-in guesses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a database that has never been analysed this writes
    /// <c>sqlite_stat1</c> for every table, which was measured at 6 - 27 ms;
    /// afterwards it proposes nothing and costs 0.0 ms. A fresh connection is
    /// enough — the pragma analyses everything while the statistics table is
    /// empty, rather than only what this connection has queried — and a fresh
    /// installation heals itself, because empty tables record nothing and the
    /// next start with data in place fills them in.
    /// </para>
    /// <para>
    /// A failure is logged and swallowed. Statistics are an optimisation, and a
    /// database that cannot be analysed can still be served — which is the one
    /// way this differs from the migration above it.
    /// </para>
    /// <para>
    /// <c>PRAGMA analysis_limit</c> is deliberately not set here or anywhere
    /// else. ADR 0056 measured it writing a selectivity wrong by three orders
    /// of magnitude on the column ADR 0032's backwards-search work set filters
    /// on, and it is per-connection, so the pool would hand it to whoever asked
    /// next.
    /// </para>
    /// </remarks>
    private async Task AnalyseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(location.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            SqlitePragmas.Apply(connection);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA optimize;";

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The database at {Database} could not be analysed. It is served anyway; queries "
                + "may pick worse plans until the maintenance routine succeeds.",
                location.FilePath);
        }
    }

    /// <summary>
    /// ADR 0039 sets the pragmas on every connection, so this asks rather than
    /// tells: <c>journal_mode</c> is the one that lives in the file, and it is
    /// also the one some network filesystems refuse. ADR 0039 measured what
    /// that costs — ADR 0018's status page drew once in twelve seconds without
    /// it, against 0.90 ms with — which is worth a warning and is not worth
    /// refusing to start over.
    /// </summary>
    private async Task WarnIfNotWriteAheadLoggingAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(location.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        SqlitePragmas.Apply(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        var mode = await command.ExecuteScalarAsync(cancellationToken) as string;

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "The database at {Database} runs in journal mode {Mode} rather than WAL. Some "
                + "network filesystems refuse it; expect readers to wait while something writes.",
                location.FilePath,
                mode);
        }
    }
}
