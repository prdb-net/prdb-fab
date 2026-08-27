using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Scheduling;

/// <summary>
/// Makes sure every routine the build knows about has a row, once, at startup.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0038 made the row the only truth about what is due, which means a
/// routine with no row is a routine that never runs. Registering code without
/// creating its row would be the quiet version of that failure, so the two
/// happen together and here.
/// </para>
/// <para>
/// It only ever inserts. A row that already exists carries a due time somebody
/// may have set by hand, and a cadence the build may have changed — so the
/// cadence is read from the code on every run rather than stored, and the row
/// keeps what is genuinely its own.
/// </para>
/// </remarks>
public sealed class RoutineRegistrar(
    FabDbContext context,
    IEnumerable<IRoutine> routines,
    TimeProvider time,
    ILogger<RoutineRegistrar> logger)
{
    public async Task EnsureRowsExistAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var added = 0;

        foreach (var routine in routines)
        {
            var exists = await context.Routines
                .AnyAsync(row => row.Name == routine.Name && row.Target == null, cancellationToken);

            if (exists)
            {
                continue;
            }

            context.Routines.Add(new RoutineRow
            {
                Name = routine.Name,
                Target = null,
                Lane = routine.Lane,

                // Due immediately. ADR 0014 spreads restarts so that a container
                // coming back does not fire everything at once; with one routine
                // there is nothing to spread, and the spread belongs with the
                // routines that make it necessary.
                DueAt = now,
            });

            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created {Count} routine row(s) that did not exist yet.", added);
        }
    }
}
