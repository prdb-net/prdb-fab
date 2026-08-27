using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Skeleton;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Skeleton;

/// <summary>
/// The skeleton's one routine: stamps the items nobody has stamped yet.
/// </summary>
/// <remarks>
/// <para>
/// Trivial and local on purpose. What it demonstrates is not the work but the
/// shape around it — ADR 0032's work set (the unstamped rows), ADR 0038's three
/// outcomes, ADR 0014's bounded run, and the rule that an empty work set is not
/// a run at all. Every one of those is a property somebody could get wrong once
/// there is real work here, and getting them wrong quietly.
/// </para>
/// <para>
/// It is scaffolding and leaves with the first real feature.
/// </para>
/// </remarks>
public sealed class SkeletonSweepRoutine(
    FabDbContext context,
    TimeProvider time,
    ILogger<SkeletonSweepRoutine> logger) : IRoutine
{
    public string Name => SkeletonSweep.RoutineName;

    public Lane Lane => Lane.Bulk;

    public TimeSpan Cadence => TimeSpan.FromSeconds(15);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var pending = await context.SkeletonItems
            .Where(row => row.SweptAt == null)
            .OrderBy(row => row.Id)
            .Take(SkeletonSweep.ItemsPerRun)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            // ADR 0032: the work set is empty, so this was never due. Returning
            // the shared instance rather than a success with a count of zero is
            // the whole of the rule — RunResult carries it, and the store
            // applies it, so no routine has to remember it.
            logger.LogDebug("Nothing to sweep.");
            return RunResult.NothingToDo;
        }

        var now = time.GetUtcNow();

        var swept = await context.SkeletonItems
            .Where(row => pending.Contains(row.Id))
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.SweptAt, now),
                cancellationToken);

        logger.LogInformation("Swept {Count} item(s).", swept);

        return RunResult.Handled(swept);
    }
}
