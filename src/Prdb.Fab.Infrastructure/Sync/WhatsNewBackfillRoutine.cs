using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The pass reading backwards into what prdb published before this installation
/// existed, bounded by a page ceiling and retiring when it reaches one.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014: bootstrap is not a state of the application. It carries a
/// resumable position of its own — a page rather than a timestamp, because
/// ADR 0013 bounds it by a page count and not by a date window — and it runs
/// beside the recurring routines from the first minute. That is what makes the
/// last step of onboarding a page rather than a progress bar: nothing waits for
/// this.
/// </para>
/// <para>
/// <strong>While it runs, that is a fact and explicitly not a Gap.</strong>
/// Nothing is broken; the catalogue is merely unfinished. Ticket 10 is where the
/// wanted list says so in a line of its own, and ADR 0018's page is where the
/// distinction is drawn for everything else.
/// </para>
/// <para>
/// In the bulk lane, which is where ADR 0014 puts backfills — and where it can
/// be, because its position is its own. It shares nothing with the fifteen-minute
/// routine beside it: that one walks a high-water mark forwards, this one walks
/// pages backwards, and neither can move the other's cursor.
/// </para>
/// <para>
/// New videos arriving while it runs push older ones further back, so a page
/// read after an insertion repeats rows the page before it already had. That
/// costs nothing and skips nothing: every write here is an upsert, and a row
/// pushed forward is a row read twice rather than a row missed. Pinning the
/// window with <c>CreatedBefore</c> would remove the repetition and add a second
/// thing to keep in the position.
/// </para>
/// </remarks>
public sealed class WhatsNewBackfillRoutine(
    WhatsNew whatsNew,
    FeedCursors cursors,
    FabDbContext context,
    IRoutineStore routines,
    ILogger<WhatsNewBackfillRoutine> logger) : IRoutine, IOneShot
{
    public const string RoutineName = "prdb.whats-new.backfill";

    public string Name => RoutineName;

    public Lane Lane => Lane.Bulk;

    /// <summary>
    /// ADR 0032's idle tick for the bulk lane. Not an interval: this routine has
    /// work until it has none, and what this says is how often to take the next
    /// turn rather than how often to act.
    /// </summary>
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    /// <summary>
    /// A backfill that has never written a position has never run. After it
    /// retires the position stays behind, so no restart starts it again — which
    /// is what keeps a bootstrap from being repeated every time the container
    /// comes back.
    /// </summary>
    public async Task<bool> StartsAsync(CancellationToken cancellationToken) =>
        !await cursors.StartedAsync(Feed.WhatsNewBackfill, cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Onboarding has not reached ADR 0010's prdb step. Not a failure,
            // and not a reason to retire: the backfill is what runs once the
            // key arrives.
            return RunResult.NothingToDo;
        }

        var page = Backfill.PageIn(await cursors.TokenAsync(Feed.WhatsNewBackfill, cancellationToken));

        if (Backfill.Beyond(page))
        {
            await RetireAsync($"it reached its ceiling of {Backfill.LastPage} pages", cancellationToken);

            return RunResult.NothingToDo;
        }

        var read = await whatsNew.ReadAsync(apiKey, createdAfter: null, page, cancellationToken);

        var written = await whatsNew.FetchAsync(apiKey, read.Unknown, cancellationToken);

        // The position is written before anything decides to stop, so a restart
        // resumes at the next page rather than repeating this one.
        await cursors.SaveAsync(Feed.WhatsNewBackfill, Backfill.Stored(page + 1), cancellationToken);

        logger.LogInformation(
            "The backfill read page {Page} of {LastPage} and brought {Count} video(s) into the catalogue.",
            page,
            Backfill.LastPage,
            written);

        if (read.Returned < Backfill.APage)
        {
            // A page short of what was asked for is the end of what prdb has,
            // which is the other way this finishes and the cheaper one.
            await RetireAsync("it reached the end of what prdb has", cancellationToken);
        }
        else if (Backfill.Beyond(page + 1))
        {
            await RetireAsync($"it reached its ceiling of {Backfill.LastPage} pages", cancellationToken);
        }

        return RunResult.Handled(written);
    }

    private async Task RetireAsync(string why, CancellationToken cancellationToken)
    {
        if (await routines.RetireAsync(Name, target: null, cancellationToken))
        {
            logger.LogInformation("The backfill has retired: {Why}.", why);
        }
    }
}
