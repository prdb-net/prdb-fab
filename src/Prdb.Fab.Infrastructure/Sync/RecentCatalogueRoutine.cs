using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Repeatedly proves and fills prdb's Catalogue side of the Recent Window.
/// One page is one bounded turn; a durable page resumes after a restart.
/// </summary>
public sealed class RecentCatalogueRoutine(
    WhatsNew whatsNew,
    FabDbContext context,
    TimeProvider time,
    ILogger<RecentCatalogueRoutine> logger) : IRoutine
{
    public const string RoutineName = "prdb.recent-window";

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey)) return RunResult.NothingToDo;

        var state = await context.RecentWindowState
            .AsTracking()
            .SingleAsync(row => row.Id == RecentWindowStateRow.TheOnlyRow, cancellationToken);
        var now = time.GetUtcNow();
        var started = state.CataloguePassStartedAt ?? now;
        var page = Math.Max(1, state.CatalogueResumePage);

        if (state.CataloguePassStartedAt is null)
        {
            state.CataloguePassStartedAt = started;
            state.CatalogueOldestCreatedAt = null;
        }

        var read = await whatsNew.ReadAsync(apiKey, createdAfter: null, page, cancellationToken);
        var written = await whatsNew.FetchAsync(apiKey, read.Unknown, cancellationToken);

        state.CatalogueOldestCreatedAt = Oldest(state.CatalogueOldestCreatedAt, read.Oldest);
        var reachedBoundary = read.Returned < CatalogueRead.APage
            || read.Oldest is { } oldest && oldest <= RecentWindow.BeginsAt(started);

        TimeSpan dueIn;
        if (reachedBoundary)
        {
            state.CatalogueCompletedAt = now;
            state.CatalogueResumePage = 1;
            state.CataloguePassStartedAt = null;
            dueIn = RecentWindow.NextPassIn(started, now);
            logger.LogInformation(
                "The prdb Recent Window completed page {Page}; {Written} new Catalogue Video(s) were written.",
                page,
                written);
        }
        else
        {
            state.CatalogueResumePage = page + 1;
            dueIn = TimeSpan.Zero;
        }

        await context.SaveChangesAsync(cancellationToken);
        return RunResult.Discovered(read.Returned, written, dueIn);
    }

    private static DateTimeOffset? Oldest(DateTimeOffset? held, DateTimeOffset? read) =>
        held is null ? read : read is null || held <= read ? held : read;
}
