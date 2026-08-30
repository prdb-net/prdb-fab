using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Upgrade preparation for the recurring Recent Window.</summary>
public sealed class RecentWindowState(FabDbContext context)
{
    private const string LegacyCatalogueBackfill = "prdb.whats-new.backfill";

    public async Task EnsureFoundationAsync(CancellationToken cancellationToken)
    {
        if (!await context.RecentWindowState.AnyAsync(cancellationToken))
        {
            context.RecentWindowState.Add(new RecentWindowStateRow());
        }

        // Both older routines were one-shot approximations of this guarantee.
        // Their source data stays; only schedule rows that would duplicate the
        // recurring window are retired on upgrade.
        await context.Routines
            .Where(row => row.Name == LegacyCatalogueBackfill
                || row.Name == DiscoveryRoutineNames.Bootstrap)
            .ExecuteDeleteAsync(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }
}
