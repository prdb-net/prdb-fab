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
