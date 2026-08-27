using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0014's cadence for the newest videos: fifteen minutes, in the sync lane.
/// </summary>
/// <remarks>
/// The reason the catalogue exists at all (ADR 0013), and the only route to it,
/// since prdb has no <c>/videos/changes</c>. One page a run, walking forwards
/// from a high-water mark that ADR 0013 sets back by an overlap before it is
/// used — <c>CreatedAfter</c> is strictly exclusive, so a mark at exactly the
/// last value seen would permanently lose every video sharing that timestamp,
/// which is precisely the bulk-import case.
/// </remarks>
public sealed class WhatsNewRoutine(
    WhatsNew whatsNew,
    FeedCursors cursors,
    FabDbContext context,
    ILogger<WhatsNewRoutine> logger) : IRoutine, ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.whats-new";

    public string Name => RoutineName;

    public Lane Lane => Lane.Sync;

    public TimeSpan Cadence => TimeSpan.FromMinutes(15);

    /// <summary>
    /// ADR 0014's fourth kind of work, and the largest single share of the idle
    /// profile: four requests an hour before anything is found.
    /// </summary>
    public PrdbWork Spends => PrdbWork.WhatsNew;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return RunResult.NothingToDo;
        }

        var mark = await cursors.PositionAsync(Feed.WhatsNew, cancellationToken);

        var page = await whatsNew.ReadAsync(apiKey, mark?.Since, page: null, cancellationToken);

        var written = await whatsNew.FetchAsync(apiKey, page.Unknown, cancellationToken);

        if (page.Newest is { } newest && (mark is null || newest > mark.At))
        {
            // Only ever forwards. The overlap means a run re-reads the last
            // minute every time, so most runs come back with a maximum that is
            // behind the mark — writing it would walk the feed backwards a
            // minute at a time and never leave.
            await cursors.SaveAsync(Feed.WhatsNew, FeedPosition.CaughtUpAt(newest), cancellationToken);
        }

        if (written > 0)
        {
            logger.LogInformation("What's New brought {Count} video(s) into the catalogue.", written);
        }

        return RunResult.Handled(written);
    }
}
