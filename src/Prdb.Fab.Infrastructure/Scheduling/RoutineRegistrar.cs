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

            // ADR 0014's bootstrap routines retire by deleting their row, so
            // for them "there is no row" is ambiguous: it is either the first
            // start or every start after they finished. Only the routine can
            // tell those apart, and this is where it is asked — creating the
            // row anyway would start the drain over on the next restart, having
            // read prdb's whole actor corpus once already.
            if (routine is IOneShot once && !await once.StartsAsync(cancellationToken))
            {
                continue;
            }

            context.Routines.Add(new RoutineRow
            {
                Name = routine.Name,
                Target = null,
                Lane = routine.Lane,

                // Due immediately, and then spread with everything else that is
                // overdue — see SpreadOverdueAsync below, which runs straight
                // after this and is what ADR 0014 asks for.
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

    /// <summary>
    /// ADR 0014's restart spread: every overdue routine is given an offset
    /// across the smaller of its own interval and five minutes, so that a
    /// container coming back does not fire everything at prdb and at every
    /// indexer in the same second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Run once, at startup, over whatever the table holds — including the rows
    /// created a moment ago. What makes the spread necessary is a container
    /// that was down: every routine in it has been overdue for as long as the
    /// downtime, so on the first tick every one of them is due at once.
    /// </para>
    /// <para>
    /// The live lane is exempt (ADR 0014): a download in flight has to be picked
    /// up at once, and nothing in that lane leaves the container.
    /// </para>
    /// </remarks>
    public async Task SpreadOverdueAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var cadences = routines.ToDictionary(routine => routine.Name, routine => routine.Cadence);

        // Tracked, because these rows are written back. Ordered by id so that
        // the same table restarted twice spreads the same way: there is nothing
        // random here to reproduce when somebody asks why a routine ran when it
        // did.
        var overdue = await context.Routines
            .AsTracking()
            .Where(row => row.DueAt <= now)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var spread = overdue.Where(row => !RestartSpread.Exempts(row.Lane)).ToList();

        if (spread.Count < 2)
        {
            // One routine is already spread. Nothing to move, and moving it
            // would only make a restart slower than no spread at all.
            return;
        }

        for (var position = 0; position < spread.Count; position++)
        {
            var row = spread[position];

            // A row naming code this build does not have has no cadence to
            // read, so it is spread across the whole window. It will not run
            // either way (ADR 0044 calls a downgrade unsupported), and the
            // alternative is leaving it due at once for no reason.
            var cadence = cadences.TryGetValue(row.Name, out var known) ? known : RestartSpread.Widest;

            row.DueAt = now + RestartSpread.OffsetFor(position, spread.Count, cadence);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Spread {Count} overdue routine(s) across the restart window.",
            spread.Count);
    }
}
