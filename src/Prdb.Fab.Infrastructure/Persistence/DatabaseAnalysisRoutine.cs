using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// Renews the query planner's statistics with a full <c>ANALYZE</c>, monthly.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0056: the startup <c>PRAGMA optimize</c> fills <c>sqlite_stat1</c> once
/// and then proposes nothing, because SQLite's own threshold for re-analysing
/// sits far above the growth an installation actually undergoes. What it misses
/// is the state every installation reaches on its own — statistics recorded
/// when the Catalogue held two hundred rows, still standing when it holds
/// hundreds of thousands — which was measured costing the Site-filtered Library
/// grid 6-7×.
/// </para>
/// <para>
/// Monthly, because a drift of tens of percent costs nothing measurable and
/// only a drift of orders of magnitude does. The bulk lane, because the run
/// takes 166 ms at a million Catalogue Videos and nothing waits on it: under
/// ADR 0039's WAL the write lock <c>ANALYZE</c> holds does not block readers.
/// </para>
/// <para>
/// There is no work set to be empty (ADR 0032), so every turn is a run: the
/// statistics are always a month older than they were.
/// </para>
/// </remarks>
public sealed class DatabaseAnalysisRoutine(
    FabDbContext context,
    ILogger<DatabaseAnalysisRoutine> logger) : IRoutine
{
    public const string RoutineName = "database.analyse";

    public string Name => RoutineName;

    public Lane Lane => Lane.Bulk;

    public TimeSpan Cadence => TimeSpan.FromDays(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync("ANALYZE;", cancellationToken);

        logger.LogInformation("Renewed the query planner's statistics.");

        return RunResult.Handled(1);
    }
}
