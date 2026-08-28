using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>The live lane's idle SABnzbd reachability check.</summary>
public sealed class SabnzbdRoutine(FabDbContext context, SabnzbdGateway sabnzbd) : IRoutine
{
    public const string RoutineName = "SABnzbd";

    public string Name => RoutineName;
    public Lane Lane => Lane.Live;
    public TimeSpan Cadence => TimeSpan.FromMinutes(5);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        // The five-second following poll is already the reachability fact while
        // work is outstanding. This slower check exists only for an idle
        // installation, so the same client is never asked twice for liveness.
        if (await context.Downloads.AnyAsync(
                row => row.State == DownloadState.Outstanding,
                cancellationToken))
        {
            return RunResult.NothingToDo;
        }

        var connection = await context.Installation.AsNoTracking().Select(row => new
        {
            row.SabnzbdUrl,
            row.SabnzbdApiKey,
        }).SingleAsync(cancellationToken);

        if (connection.SabnzbdUrl is null || connection.SabnzbdApiKey is null)
        {
            return RunResult.NothingToDo;
        }

        var categories = await sabnzbd.CategoryNamesAsync(
            connection.SabnzbdUrl,
            connection.SabnzbdApiKey,
            cancellationToken);

        return categories.Outcome == SabnzbdConnectionOutcome.Saved
            ? RunResult.Discovered(categories.Categories.Count, rowsAdded: 0)
            : RunResult.Failed("SABnzbd did not answer its category check.");
    }
}
