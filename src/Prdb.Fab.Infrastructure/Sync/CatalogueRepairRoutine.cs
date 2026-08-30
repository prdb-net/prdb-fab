using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Current and pinned videos read back from <c>POST /videos/batch</c>, fifty a
/// request, oldest-checked first, in the bulk lane.
/// </summary>
/// <remarks>
/// <para>
/// The only thing that keeps the catalogue correct, because videos are the one
/// entity with no feed. <strong>Two holes, one read.</strong> Video metadata
/// edits have no feed at all; video image rows are hard-deleted upstream and
/// simply stop being returned, so a removal is invisible to the images feed.
/// <c>VideoDetailDto</c> carries the authoritative <c>images[]</c>, so diffing
/// that against the local copy finds the removed artwork and the rest of the
/// payload finds the correction — which is why this is one pass and not two,
/// and why it writes through <see cref="VideoDetails"/> like What's New does
/// rather than having a writer of its own.
/// </para>
/// <para>
/// ADR 0050 repairs every row in the Recent Window. Outside it, ADR 0013's
/// original rule remains: only a row <see cref="CataloguePins"/> says is pinned
/// is a lasting repair obligation.
/// </para>
/// <para>
/// <strong>Steered by a request budget and not by a cadence.</strong> The
/// cadence below is ADR 0032's idle tick for the bulk lane — how often to take
/// the next turn rather than how often to act — and what a turn costs is
/// <see cref="RepairBudget"/>'s to say. The governor holds the line at half the
/// hourly limit; this holds the other half of ADR 0014's sentence, which is
/// that a small plan still asks once rather than computing itself down to
/// nothing while repair sits last in the order of precedence.
/// </para>
/// <para>
/// <strong>Held entry files follow the repaired catalogue.</strong> After the
/// catalogue write, the sidecar is replaced only when its rendered contents
/// changed and the entry image only when the chosen artwork changed or is
/// missing. Recorded and physical video paths never move during repair.
/// </para>
/// </remarks>
public sealed class CatalogueRepairRoutine(
    FabDbContext context,
    CataloguePins pins,
    PrdbGateway prdb,
    PrdbGovernor governor,
    VideoDetails details,
    EntryFiles entryFiles,
    TimeProvider time,
    ILogger<CatalogueRepairRoutine> logger) : IRoutine
{
    public const string RoutineName = "prdb.repair";

    public string Name => RoutineName;

    /// <summary>ADR 0014 puts the repair pass in the bulk lane.</summary>
    public Lane Lane => Lane.Bulk;

    /// <summary>
    /// ADR 0032's idle tick for the bulk lane, and deliberately not an interval:
    /// this routine's work set is current or pinned Catalogue rows, so what
    /// this says is how often to take the next turn. How much a turn spends is
    /// the budget's, which is what ADR 0013 asked for.
    /// </summary>
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Onboarding has not reached ADR 0010's prdb step. Nothing is
            // broken and nothing is pinned yet either.
            return RunResult.NothingToDo;
        }

        var budget = governor.LastReading;

        var now = time.GetUtcNow();
        var recentSince = RecentWindow.BeginsAt(now);
        var staleBefore = now - RecentWindow.RevalidateAfter;

        // Oldest-checked first, which is what LastReadAt is on the row for. The
        // id breaks the tie so that a catalogue whose rows were all written in
        // one import is walked in a stable order rather than in whatever
        // order SQLite finds them.
        var pinnedIds = pins.Pinned(context.CatalogueVideos).Select(row => row.Id);
        var due = await context.CatalogueVideos
            .Where(row => row.LastReadAt <= staleBefore
                && (row.CreatedAtUtc >= recentSince || pinnedIds.Contains(row.Id)))
            .OrderBy(row => row.LastReadAt)
            .ThenBy(row => row.Id)
            .Select(row => new Due(row.Id, row.PrdbId))
            .Take(RepairBudget.VideosFor(budget))
            .ToListAsync(cancellationToken);

        if (due.Count == 0)
        {
            // Nothing current or pinned is due. ADR 0032: an empty work set is
            // not a run, so this is not recorded and moves no counter.
            return RunResult.NothingToDo;
        }

        var repaired = 0;

        foreach (var batch in due.Chunk(RepairBudget.ABatch))
        {
            repaired += await ReadBackAsync(apiKey, batch, cancellationToken);
        }

        logger.LogInformation(
            "The repair pass re-read {Count} current or pinned video(s) of the {Due} it asked about.",
            repaired,
            due.Count);

        return RunResult.Handled(repaired);
    }

    /// <summary>
    /// One request, and what it leaves behind.
    /// </summary>
    /// <remarks>
    /// Nothing is caught. A refusal is the lane's to read as a failure and a
    /// deferral is the lane's to read as neither (ADR 0014, ADR 0038), and a
    /// deferral part-way through costs nothing: every write below is an upsert
    /// and the next run asks from the same work set.
    /// </remarks>
    private async Task<int> ReadBackAsync(
        string apiKey,
        IReadOnlyList<Due> batch,
        CancellationToken cancellationToken)
    {
        var read = await prdb.AskAsync(
            apiKey,
            PrdbWork.Repair,
            (client, token) => client.Videos.Batch.PostAsync(
                new GetVideosByIdsRequest { Ids = [.. batch.Select(video => (Guid?)video.PrdbId)] },
                cancellationToken: token),
            cancellationToken);

        var answered = new HashSet<Guid>();

        foreach (var detail in read ?? [])
        {
            var previousImage = detail.Id is { } videoId
                ? await entryFiles.ChosenImageIdAsync(videoId, cancellationToken)
                : null;

            await details.WriteAsync(detail, cancellationToken);

            if (detail.Id is { } prdbId)
            {
                answered.Add(prdbId);

                var currentImage = await entryFiles.ChosenImageIdAsync(
                    prdbId,
                    cancellationToken);
                await entryFiles.RefreshAsync(
                    prdbId,
                    previousImage != currentImage,
                    cancellationToken);
            }
        }

        await MissedAsync(batch, answered, cancellationToken);

        return answered.Count;
    }

    /// <summary>
    /// Moves <em>last re-read</em> on the rows prdb did not answer for.
    /// </summary>
    /// <remarks>
    /// The endpoint omits ids that do not exist rather than refusing them,
    /// which is what makes it safe to ask about whatever is pinned — a video
    /// deleted at prdb is one fewer row in the answer and not an error. But the
    /// order this pass walks in is <em>oldest-checked first</em>, so a pinned
    /// row prdb has stopped knowing about would sit at the front of it forever
    /// and repair would ask about the same fifty until somebody noticed. The
    /// stamp says when the row was last asked about, and that is what is
    /// written here; the row itself is left exactly as it was, because nothing
    /// was learned about it.
    /// </remarks>
    private async Task MissedAsync(
        IReadOnlyList<Due> batch,
        HashSet<Guid> answered,
        CancellationToken cancellationToken)
    {
        var missed = batch
            .Where(video => !answered.Contains(video.PrdbId))
            .Select(video => video.Id)
            .ToList();

        if (missed.Count == 0)
        {
            return;
        }

        var asked = time.GetUtcNow();

        await context.CatalogueVideos
            .Where(row => missed.Contains(row.Id))
            .ExecuteUpdateAsync(
                row => row.SetProperty(video => video.LastReadAt, asked),
                cancellationToken);

        logger.LogWarning(
            "prdb did not answer for {Count} pinned video(s) the repair pass asked about.",
            missed.Count);
    }

    /// <summary>A row the pass is about to ask about, by both of its names.</summary>
    private sealed record Due(long Id, Guid PrdbId);
}
