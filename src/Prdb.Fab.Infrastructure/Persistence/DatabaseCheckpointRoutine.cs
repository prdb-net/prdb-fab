using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// Folds the write-ahead log back into the database and truncates it, daily.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0039 opened the database in WAL and said nothing about emptying the log
/// it created. SQLite's own answer is <c>wal_autocheckpoint</c>, which tries
/// every thousand pages and gives up silently whenever a reader is in the way —
/// and in a tool whose four lanes read continuously, something usually is.
/// ADR 0059 measured the log at ~37 MB, which is a file every read walks
/// past.
/// </para>
/// <para>
/// <c>TRUNCATE</c> rather than <c>PASSIVE</c>, because the size is the point: a
/// passive checkpoint copies the frames back and leaves the file as large as it
/// found it. It waits on the readers rather than skipping them, which ADR 0039's
/// <c>busy_timeout</c> already makes the shape of every other write here — and
/// when the wait runs out it returns busy, the log stays as it was, and the next
/// day's turn tries again. Nothing is lost either way, which is why this needs
/// no failure of its own.
/// </para>
/// <para>
/// Daily, in the bulk lane. There is no work set to be empty (ADR 0032), so
/// every turn is a run: the log is always a day's writes longer than it was.
/// </para>
/// </remarks>
public sealed class DatabaseCheckpointRoutine(
    FabDbContext context,
    ILogger<DatabaseCheckpointRoutine> logger) : IRoutine
{
    public const string RoutineName = "database.checkpoint";

    public string Name => RoutineName;

    public Lane Lane => Lane.Bulk;

    public TimeSpan Cadence => TimeSpan.FromDays(1);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(
            "PRAGMA wal_checkpoint(TRUNCATE);",
            cancellationToken);

        logger.LogInformation("Checkpointed the write-ahead log.");

        return RunResult.Handled(1);
    }
}
