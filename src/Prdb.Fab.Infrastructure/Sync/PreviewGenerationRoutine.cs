using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0064's later decode: makes the Sprite Sheet and WebVTT for one owed
/// Video File per run, and commits the pair as something to send.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One file per run, in the Bulk lane.</strong> The lane is where work
/// goes that nothing waits on, and one file is what makes this yield: a decode
/// is minutes of CPU, and collecting — which is somebody's download finishing —
/// runs in a lane of its own but shares the machine. Being behind is answered
/// by coming round again, which is the shape ADR 0032 gives every bounded pass.
/// </para>
/// <para>
/// <strong>It spends no prdb budget.</strong> Nothing here reaches the network:
/// the file is local, ffmpeg is bundled, and the only remote act in this
/// channel is the upload, which is its own routine with its own share. So this
/// asks the governor nothing, and what stands in its place is the contract's
/// timeout, its size ceiling and the count of pairs already waiting.
/// </para>
/// <para>
/// <strong>Nothing is decoded inside a transaction.</strong> The row is read,
/// the decode happens with nothing held, and the result is written afterwards —
/// the same discipline ADR 0026 keeps for the File lane, for the same reason: a
/// twenty-minute transaction is a twenty-minute writer lock on a database
/// everything else shares.
/// </para>
/// <para>
/// <strong>The commit is the row's state.</strong> Both halves are written and
/// renamed first, and the row moves to <c>Ready</c> last; a crash anywhere
/// earlier leaves files nothing claims, which the sweep at the top of the next
/// run takes. That is the same order <see cref="PreviewAssetCache"/> keeps in
/// the other direction, and it is why neither has to reason about half a pair.
/// </para>
/// </remarks>
public sealed class PreviewGenerationRoutine(
    FabDbContext context,
    PreviewPublications publications,
    PreviewBackfill backfill,
    PublicationStore store,
    ISpriteSheetProcess sheets,
    VideoFileMover mover,
    TimeProvider time,
    ILogger<PreviewGenerationRoutine> logger) : IRoutine, IWorkSetPaced
{
    public const string RoutineName = "prdb.preview-generation";

    /// <summary>
    /// How many rows one run may settle without decoding anything.
    /// </summary>
    /// <remarks>
    /// Expiries and the rows of an account that is no longer this one cost a
    /// write each and no work, but a backlog of them is still a backlog: a
    /// bounded batch keeps the lane's turn short whatever it finds.
    /// </remarks>
    private const int ABatch = 200;

    public string Name => RoutineName;

    public Lane Lane => Lane.Bulk;

    public TimeSpan Cadence => PreviewPublicationContract.GenerationCadence;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        // The work set, asked before anything else: an installation that has
        // never filed an identified file and has asked for nothing spends
        // nothing on this routine. A request counts even before it has taken
        // anything up, because taking one up is this routine's work too.
        if (!await context.PreviewPublications.AnyAsync(
                row => row.State == PreviewPublicationState.Intended,
                cancellationToken)
            && !await context.PreviewBackfills.AnyAsync(
                row => row.State == PreviewBackfillState.Running,
                cancellationToken))
        {
            return RunResult.NothingToDo;
        }

        var installation = await context.Installation
            .AsNoTracking()
            .Select(row => new
            {
                row.PublishGeneratedPreviews,
                row.PreviewPublicationExplainedAt,
                row.PrdbUserHash,
            })
            .SingleAsync(cancellationToken);

        // Both Brakes, and neither is a failure: the channel is off, or it is
        // on and nobody has yet been told what it sends. The Status page says
        // so for the second (ADR 0064), and the rows wait either way.
        if (!installation.PublishGeneratedPreviews
            || installation.PreviewPublicationExplainedAt is null
            || string.IsNullOrWhiteSpace(installation.PrdbUserHash))
        {
            return RunResult.NothingToDo;
        }

        var settled = await SettleAsync(installation.PrdbUserHash, cancellationToken);

        settled += await backfill.AbandonAsync(installation.PrdbUserHash, cancellationToken);

        await ReclaimAsync(cancellationToken);

        // ADR 0064's Brake on the transient store: fifty generated pairs are
        // waiting for their own uploads, and the backlog drains before another
        // is made. It is also what bounds a backfill — it drains through the
        // ceiling rather than around it — so this is asked before anything is
        // taken up as well as before anything is decoded.
        if (await context.PreviewPublications.CountAsync(
                row => row.State == PreviewPublicationState.Ready,
                cancellationToken) >= PreviewPublicationContract.MostWaiting)
        {
            return settled > 0 ? RunResult.Handled(settled) : RunResult.NothingToDo;
        }

        var owed = await NextAsync(installation.PrdbUserHash, cancellationToken);

        if (owed is null)
        {
            // Nothing owed, so this is the run that takes a file up — and it is
            // the only thing that ever selects one out of the existing Library.
            // Taking one up writes an intent, which the read below then finds.
            if (await backfill.TakeUpAsync(installation.PrdbUserHash, cancellationToken))
            {
                owed = await NextAsync(installation.PrdbUserHash, cancellationToken);
            }
        }

        if (owed is null)
        {
            return settled > 0 ? RunResult.Handled(settled) : RunResult.NothingToDo;
        }

        return await GenerateAsync(owed, settled, cancellationToken);
    }

    /// <summary>
    /// The next preview to decode: what Filing owes before what anybody asked
    /// for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The order is the whole of how a backfill stays out of the
    /// way.</strong> ADR 0064's automatic scope is the file that has just been
    /// filed, and a person who files one while five thousand historical ones
    /// are queued would otherwise wait a week for the picture of the file they
    /// were actually watching. An intent with no request in front of it goes
    /// first, whatever the clock says.
    /// </para>
    /// <para>
    /// A row belonging to a request that is paused is passed over rather than
    /// given up: pausing holds work back and gives nothing up, which is what
    /// makes resuming mean anything.
    /// </para>
    /// </remarks>
    private async Task<PreviewPublicationRow?> NextAsync(
        string userHash,
        CancellationToken cancellationToken)
    {
        var owed = context.PreviewPublications
            .AsTracking()
            .Where(row => row.State == PreviewPublicationState.Intended
                          && row.UserHash == userHash);

        return await owed
                   .Where(row => row.BackfillId == null)
                   .OrderBy(row => row.IntendedAt)
                   .ThenBy(row => row.Id)
                   .FirstOrDefaultAsync(cancellationToken)
               ?? await owed
                   .Where(row => context.PreviewBackfills.Any(
                       request => request.Id == row.BackfillId
                                  && request.State == PreviewBackfillState.Running))
                   .OrderBy(row => row.IntendedAt)
                   .ThenBy(row => row.Id)
                   .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// What a finished decode hands back, at the cadence the run itself knows
    /// to be right.
    /// </summary>
    /// <remarks>
    /// ADR 0064's five minutes is sized for work that arrives one filed file at
    /// a time and that nothing waits on. A backfill is neither: it is a finite
    /// set somebody asked for and is watching, and at five minutes a file a
    /// Library of any size would outlast most subscriptions. So a run that got
    /// through a file of somebody's request asks to be seen again at
    /// <see cref="PreviewPublicationContract.BackfillCadence"/>, which is the
    /// uploading routine's cadence — the two halves of the pipeline then run at
    /// one speed, and <see cref="PreviewPublicationContract.MostWaiting"/> is
    /// what actually bounds the pace.
    /// </remarks>
    private static RunResult Generated(int items, bool forARequest) =>
        forARequest
            ? RunResult.Handled(items, PreviewPublicationContract.BackfillCadence)
            : RunResult.Handled(items);

    /// <summary>
    /// Gives up on what can no longer become a publication, before anything is
    /// decoded.
    /// </summary>
    /// <remarks>
    /// Two reasons, both ADR 0064's. An intent nothing has touched for thirty
    /// days belongs to an installation that was switched off, and the file it
    /// describes has had a month to become a different file. An intent made
    /// under another account is one this key must never send: the account is
    /// part of the row's identity, and a new account starts having published
    /// nothing.
    /// </remarks>
    private async Task<int> SettleAsync(string userHash, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var stale = now - PreviewPublicationContract.IntentExpiry;

        var giving = await context.PreviewPublications
            .AsTracking()
            .Where(row => row.State == PreviewPublicationState.Intended
                          && (row.IntendedAt < stale || row.UserHash != userHash))
            .OrderBy(row => row.IntendedAt)
            .Take(ABatch)
            .ToListAsync(cancellationToken);

        foreach (var row in giving)
        {
            Drop(
                row,
                row.UserHash == userHash
                    ? "The intent expired before it was generated."
                    : "The prdb account changed before it was generated.",
                now);
        }

        if (giving.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return giving.Count;
    }

    /// <summary>
    /// Takes back the bytes of every decode that did not finish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cancelled or timed-out decode wrote nothing, a restart mid-write left
    /// a part file, and a row that has been dropped or sent owns nothing any
    /// more. What is claimed is exactly the rows that have committed a pair and
    /// not yet finished with it — which is a query rather than a bookkeeping
    /// note, so there is nothing to keep in step.
    /// </para>
    /// <para>
    /// The three claiming states are the three that can hold bytes: a pair
    /// waiting to be sent, one whose request has left, and one whose outcome
    /// nobody could establish. The middle one exists only between two writes of
    /// the uploading routine, and it is in the query because a sweep that
    /// deleted the bytes under a request in flight would leave nothing for the
    /// person who has to decide about it.
    /// </para>
    /// </remarks>
    private async Task ReclaimAsync(CancellationToken cancellationToken)
    {
        var claimed = await context.PreviewPublications
            .Where(row => row.State == PreviewPublicationState.Ready
                          || row.State == PreviewPublicationState.Sending
                          || row.State == PreviewPublicationState.Uncertain)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        var reclaimed = store.Reclaim(claimed.ToHashSet());

        if (reclaimed > 0)
        {
            logger.LogInformation(
                "Reclaimed {Count} file(s) left by a generation that did not finish.",
                reclaimed);
        }
    }

    private async Task<RunResult> GenerateAsync(
        PreviewPublicationRow owed,
        int settled,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var file = await context.VideoFiles
            .AsNoTracking()
            .Where(row => row.Id == owed.VideoFileId)
            .Select(row => new { row.FiledPath, row.SizeBytes, row.RuntimeSeconds, row.OsHash })
            .SingleOrDefaultAsync(cancellationToken);

        if (file is null || file.RuntimeSeconds is not > 0)
        {
            return await GivenUpAsync(
                owed,
                settled,
                "The Video File is no longer in the Library, or has no measured Runtime.",
                now,
                cancellationToken);
        }

        // ADR 0064: eligibility is a claim about one osHash, and the file at
        // the recorded path may have been replaced or edited since. Verified
        // here rather than trusted, because the alternative is publishing a
        // picture of one file under the hash of another.
        if (!Identical(file.FiledPath, file.SizeBytes, owed.OsHash))
        {
            return await GivenUpAsync(
                owed,
                settled,
                "The Video File's bytes are not the ones the preview was intended for.",
                now,
                cancellationToken);
        }

        // Asked here rather than at the intent, because the read that fills
        // this population is scheduled by the same filing and usually lands
        // afterwards. prdb's population is short of sheets; this file's is
        // already there.
        if (await publications.AlreadyShownAsync(owed.OsHash, cancellationToken))
        {
            return await GivenUpAsync(
                owed,
                settled,
                "prdb already shows a sprite sheet made from this exact file.",
                now,
                cancellationToken);
        }

        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(file.RuntimeSeconds.Value));

        FfmpegCaptureResult run;

        try
        {
            run = await sheets.RunAsync(file.FiledPath, plan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The container is stopping. The row is untouched and the bytes,
            // if any reached the disk, are taken by the next run's sweep.
            return RunResult.Interrupted(settled);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return await FailedAsync(owed, $"The decode could not be started: {failed.Message}", now, cancellationToken);
        }

        if (!run.Completed)
        {
            var why = run.TimedOut
                ? $"The decode did not finish inside {PreviewPublicationContract.Generation.TotalMinutes:0} minutes."
                : run.TooLarge
                    ? "The generated sheet was larger than the publication contract allows."
                    : $"ffmpeg failed: {Sentence(run.Error)}";

            return await FailedAsync(owed, why, now, cancellationToken);
        }

        if (Refuse(run.Bytes, plan) is { } refusal)
        {
            return await FailedAsync(owed, refusal, now, cancellationToken);
        }

        var vtt = plan.Vtt(PreviewPublicationContract.SheetFilename);

        if (vtt.LongLength > PreviewPublicationContract.AVtt)
        {
            return await FailedAsync(owed, "The generated WebVTT was larger than the publication contract allows.", now, cancellationToken);
        }

        // The shared validator, used on this tool's own output exactly as it is
        // used on somebody else's: the sheet's real geometry read off the file,
        // the cues read against it. A pair this tool would refuse to show is
        // one prdb's population is better off without.
        var timeline = SpriteTimeline.Read(vtt, (plan.Width, plan.Height), PreviewPublicationContract.MostTiles);

        if (!timeline.Usable || timeline.Tiles.Count != plan.Tiles)
        {
            return await FailedAsync(
                owed,
                $"The generated pair did not validate: {timeline.Reason ?? "the cues do not match the sheet."}",
                now,
                cancellationToken);
        }

        try
        {
            await store.WritePairAsync(owed.Id, run.Bytes, vtt, cancellationToken);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            // A full or unwritable data directory. Whatever half reached the
            // disk belongs to a row that still says Intended, so the next run's
            // sweep takes it before this one is tried again.
            return await FailedAsync(owed, $"The generated pair could not be written: {failed.Message}", now, cancellationToken);
        }

        owed.State = PreviewPublicationState.Ready;
        owed.Tiles = plan.Tiles;
        owed.Columns = plan.Columns;
        owed.Rows = plan.Rows;
        owed.SheetBytes = run.Bytes.LongLength;
        owed.GeneratedAt = now;
        owed.Note = null;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Generated a {Tiles}-tile preview of {Bytes} bytes for publication.",
            plan.Tiles,
            run.Bytes.LongLength);

        return Generated(settled + 1, owed.BackfillId is not null);
    }

    /// <summary>
    /// Why this sheet is not the sheet that was asked for, or null where it is.
    /// </summary>
    /// <remarks>
    /// The geometry is read out of the JPEG's own frame header rather than
    /// taken from what was asked for, which is the check ADR 0061 makes of a
    /// sheet somebody else uploaded and is worth just as much here: a decode
    /// that produced a different grid produced a picture whose cues point at
    /// the wrong seconds, and nothing on screen would say so.
    /// </remarks>
    private static string? Refuse(byte[] sheet, SpriteSheetPlan plan)
    {
        if (sheet.LongLength > PreviewPublicationContract.ASheet)
        {
            return "The generated sheet was larger than the publication contract allows.";
        }

        if (JpegGeometry.Of(sheet) is not { } geometry)
        {
            return "The generated sheet is not a readable JPEG.";
        }

        return geometry == (plan.Width, plan.Height)
            ? null
            : $"The generated sheet is {geometry.Width}x{geometry.Height} rather than {plan.Width}x{plan.Height}.";
    }

    /// <summary>
    /// A decode that did not work: counted, and given up on once it has not
    /// worked <see cref="PreviewPublicationContract.MostAttempts"/> times.
    /// </summary>
    private async Task<RunResult> FailedAsync(
        PreviewPublicationRow owed,
        string why,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        owed.Attempts++;
        owed.Note = why;

        if (owed.Attempts >= PreviewPublicationContract.MostAttempts)
        {
            Drop(owed, why, now);
        }

        await context.SaveChangesAsync(cancellationToken);

        // A failure rather than a handled run, because a decode that did not
        // work is a thing a person reading the run log should see. The lane's
        // backoff is what keeps it from being tried again immediately.
        return RunResult.Failed(why);
    }

    /// <summary>
    /// The file cannot produce the publication that was intended, and nothing
    /// about coming back later changes that.
    /// </summary>
    private async Task<RunResult> GivenUpAsync(
        PreviewPublicationRow owed,
        int settled,
        string why,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var forARequest = owed.BackfillId is not null;

        Drop(owed, why, now);

        await context.SaveChangesAsync(cancellationToken);

        // Succeeded with a note rather than failed: nothing went wrong, and the
        // sentence is what a person reading the log is owed. A request's file
        // that could not be made keeps the request's cadence — a Library with a
        // hundred files that have moved is exactly the case that must not take
        // an afternoon to walk past.
        return forARequest
            ? RunResult.Handled(settled + 1, PreviewPublicationContract.BackfillCadence, why)
            : RunResult.Handled(settled + 1, why);
    }

    private void Drop(PreviewPublicationRow owed, string why, DateTimeOffset now)
    {
        owed.State = PreviewPublicationState.Dropped;
        owed.Note = why;
        owed.SettledAt = now;

        store.Delete(owed.Id);
    }

    private bool Identical(string path, long sizeBytes, string osHash)
    {
        try
        {
            return File.Exists(path) && mover.Matches(path, sizeBytes, osHash);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// ffmpeg's last line of standard error, which is the one that says what
    /// went wrong, bounded so a run log entry stays a sentence.
    /// </summary>
    private static string Sentence(string error)
    {
        var lines = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var last = lines.Length == 0 ? "it produced no picture and said nothing." : lines[^1];

        return last.Length <= 200 ? last : last[..200];
    }
}
