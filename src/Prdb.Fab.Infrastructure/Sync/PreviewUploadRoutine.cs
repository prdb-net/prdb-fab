using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0064's delivery: submits one generated pair per run, and records what
/// became of it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One upload per run, in the Bulk lane, on
/// <see cref="PrdbWork.Publications"/>.</strong> The share is the ADR's
/// amendment to ADR 0061 rather than a choice made here: a publication carries
/// megabytes, and left in <c>Writes</c> a backlog of them would occupy the
/// reserve that exists to keep a Fulfilment report moving. One at a time
/// because the reserve is a share of an hourly budget rather than a concurrency
/// limit, and because the wire is somebody else's too.
/// </para>
/// <para>
/// <strong>The row is written before the request leaves and again after it
/// returns</strong>, and the gap between those two writes is the whole design.
/// A submission cannot be looked up: prdb's three read endpoints answer with
/// publicly visible rows only, and a row in moderation is not one — so an
/// answer that never arrives leaves a question nothing here can settle. The
/// first write is what makes that question visible instead of invisible:
/// <see cref="PreviewPublicationState.Sending"/> is committed before the POST,
/// so a container that stops mid-flight is found rather than assumed.
/// </para>
/// <para>
/// <strong>Nothing is ever sent twice on this tool's own initiative.</strong>
/// Not after a timeout, not after a <c>500</c>, not after a restart. The API
/// exposes no idempotency key, so a retry is a coin-flip between one public
/// picture and two with no retraction available either way — and ADR 0064
/// weighs that asymmetry and decides it. What the tool does instead is keep the
/// bytes, name the reason and wait for a person.
/// </para>
/// <para>
/// <strong>Deferral is not an outcome.</strong> The governor is asked before
/// anything is written, and a <c>429</c> or <c>503</c> leaves the row exactly
/// as it was — which is ADR 0014's fourth case and the reason the state
/// enumeration has no member for it.
/// </para>
/// </remarks>
public sealed class PreviewUploadRoutine(
    FabDbContext context,
    PreviewPublications publications,
    PublicationStore store,
    PrdbGateway prdb,
    PrdbGovernor governor,
    TimeProvider time,
    ILogger<PreviewUploadRoutine> logger) : IRoutine, ISpendsPrdbBudget, IWorkSetPaced
{
    public const string RoutineName = "prdb.preview-upload";

    /// <summary>
    /// How many rows one run may settle without sending anything.
    /// </summary>
    /// <remarks>
    /// The generating routine's figure and its reason: an account change or a
    /// restart can leave a batch of rows needing a write each and no work, and
    /// a bounded batch keeps the lane's turn short whatever it finds.
    /// </remarks>
    private const int ABatch = 200;

    public string Name => RoutineName;

    public Lane Lane => Lane.Bulk;

    public TimeSpan Cadence => PreviewPublicationContract.UploadCadence;

    /// <summary>
    /// ADR 0064's new kind of work, and the one the shedding table has no entry
    /// for.
    /// </summary>
    /// <remarks>
    /// Declared because it is true — this routine spends prdb requests on a
    /// clock — and it counts for nothing in <c>IdleProfile</c> because it is
    /// also work-set paced: an installation with nothing generated spends
    /// nothing here. It needs no shed cadence of its own either, and that is
    /// the reserve doing the work rather than an omission: at 40 % a small plan
    /// defers every upload long before the schedule would think about slowing
    /// one down.
    /// </remarks>
    public PrdbWork Spends => PrdbWork.Publications;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        // The work set, asked before anything else: an installation that has
        // published nothing and generated nothing spends nothing here. Sending
        // counts because a row left in it by a stopped container is work of its
        // own, and Uncertain because a later read may have settled one.
        if (!await context.PreviewPublications.AnyAsync(
                row => row.State == PreviewPublicationState.Ready
                       || row.State == PreviewPublicationState.Sending
                       || row.State == PreviewPublicationState.Uncertain,
                cancellationToken))
        {
            return RunResult.NothingToDo;
        }

        // What a restart left behind, before the switch is read: a row found
        // mid-flight is an unanswered request whatever the channel now says,
        // and leaving it as Sending would make the next run send it.
        var settled = await StrandedAsync(cancellationToken);

        var installation = await context.Installation
            .AsNoTracking()
            .Select(row => new
            {
                row.PublishGeneratedPreviews,
                row.PreviewPublicationExplainedAt,
                row.PrdbApiKey,
                row.PrdbUserHash,
            })
            .SingleAsync(cancellationToken);

        settled += await ReconciledAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(installation.PrdbUserHash)
            || string.IsNullOrWhiteSpace(installation.PrdbApiKey))
        {
            // Not an installation that failed; one that is not connected. The
            // rows wait, and the expiry settles them if it stays that way.
            return Done(settled);
        }

        settled += await AbandonedAsync(installation.PrdbUserHash, cancellationToken);

        // Both Brakes, and neither is a failure: the channel is off, or it is
        // on and nobody has been told yet what it sends. Generated rows wait —
        // ADR 0064 is explicit that switching off stops sending rather than
        // dropping what is already made.
        if (!installation.PublishGeneratedPreviews
            || installation.PreviewPublicationExplainedAt is null)
        {
            return Done(settled);
        }

        var owed = await context.PreviewPublications
            .AsTracking()
            .Where(row => row.State == PreviewPublicationState.Ready
                          && row.UserHash == installation.PrdbUserHash)
            .OrderBy(row => row.GeneratedAt)
            .ThenBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (owed is null)
        {
            return Done(settled);
        }

        // Asked here rather than left to the handler, because the answer
        // decides whether the row is marked as being in flight at all. A
        // deferral must leave it untouched, and the cheapest way to promise
        // that is to not write anything.
        if (governor.Ask(PrdbWork.Publications) is { Sends: false } held)
        {
            return RunResult.Deferred(held.Wait, held.Reason);
        }

        return await SendAsync(owed, installation.PrdbApiKey, settled, cancellationToken);
    }

    /// <summary>
    /// Sends one generated pair, and records the outcome.
    /// </summary>
    private async Task<RunResult> SendAsync(
        PreviewPublicationRow owed,
        string apiKey,
        int settled,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        using var sheet = store.OpenSheet(owed.Id);
        using var vtt = store.OpenVtt(owed.Id);

        if (sheet is null || vtt is null)
        {
            // The bytes are gone and nothing was sent. Back to the intent, so
            // that the generating routine makes them again — which is safe
            // precisely because a Ready row has never left the machine. It is
            // also what a Restore arrives as: the row crosses the boundary and
            // the bytes deliberately do not.
            owed.State = PreviewPublicationState.Intended;
            owed.Note = "The generated pair was not on disk, so it is being made again.";
            owed.Tiles = null;
            owed.Columns = null;
            owed.Rows = null;
            owed.SheetBytes = null;
            owed.GeneratedAt = null;

            await context.SaveChangesAsync(cancellationToken);

            return RunResult.Handled(settled + 1, owed.Note);
        }

        // ADR 0064: the switch and the account are read again here, as late as
        // it is possible to read them. Everything above this line took time,
        // and what a person turned off while a decode was running must not go
        // out because a routine read the setting before the decode.
        if (!await StillSendingAsync(owed.UserHash, apiKey, cancellationToken))
        {
            return Done(settled);
        }

        // The one write that has to happen before the request, and the reason
        // the whole uncertain state is reachable: after this commit, a crash is
        // a question rather than a silence.
        owed.State = PreviewPublicationState.Sending;
        owed.Note = null;

        await context.SaveChangesAsync(cancellationToken);

        SubmitVideoUserImageResponse? accepted;

        try
        {
            accepted = await prdb.AskAsync(
                apiKey,
                PrdbWork.Publications,
                (client, token) => client.VideoUserImages.PostAsync(
                    PreviewPublicationBody.For(owed.VideoPrdbId, owed.OsHash, sheet, vtt),
                    cancellationToken: token),
                cancellationToken);
        }
        catch (PrdbDeferredException deferred)
        {
            // The handler turned it away after the governor had said yes —
            // another lane spent the budget, or prdb answered 429 in between.
            // Nothing left, so the row goes back exactly as it was.
            owed.State = PreviewPublicationState.Ready;

            await context.SaveChangesAsync(cancellationToken);

            return RunResult.Deferred(deferred.Wait, deferred.Deferral);
        }
        catch (ApiException refused)
        {
            return await AnsweredAsync(owed, refused, settled, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The container is stopping, and the request may have arrived. The
            // row stays as Sending — writing anything now would mean a write
            // with a token that is already cancelled — and the next run's first
            // pass reads it as exactly the question it is.
            return RunResult.Interrupted(settled);
        }
        catch (Exception unanswered) when (unanswered is HttpRequestException or TaskCanceledException
                                           or IOException or OperationCanceledException)
        {
            // The request left and nothing came back that says what became of
            // it — including the case where this container is being stopped.
            // ADR 0064: absence from prdb's read endpoints proves nothing, so
            // this is a question and never a reason to send again.
            return await UncertainAsync(
                owed,
                $"The upload was not answered: {Sentence(unanswered)}. "
                + "Whether prdb accepted it cannot be established.",
                settled,
                now,
                cancellationToken);
        }

        if (accepted?.VideoUserImageId is not { } imageId)
        {
            // A 2xx with nothing in it. prdb accepted something and this tool
            // cannot say what, which is the uncertain case rather than a
            // success with a missing field: sending again would risk the
            // duplicate, and calling it sent would lose the id.
            return await UncertainAsync(
                owed,
                "prdb accepted the upload without naming the image it created.",
                settled,
                now,
                cancellationToken);
        }

        owed.State = PreviewPublicationState.Sent;
        owed.PrdbImageId = imageId;
        owed.ModerationTargetId = accepted.ModerationTargetId;
        owed.SubmittedUnder = UserPreviewModeration.Signature(
            accepted.ModerationStatus,
            accepted.ModerationVisibility);
        owed.SettledAt = now;

        // Submitted, not published. ADR 0064 keeps those words apart until a
        // read endpoint has returned the row, which is the only evidence
        // moderation has let it through.
        owed.Note = "Submitted to prdb and waiting for moderation.";

        // Accepted, so the bytes have done their work. Regenerating them is
        // possible and resubmitting them is not, which is what makes this the
        // one place they can go without a decision being lost.
        store.Delete(owed.Id);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Submitted a generated preview to prdb as {ImageId}, entering moderation as {Signature}.",
            imageId,
            owed.SubmittedUnder);

        return RunResult.Handled(settled + 1);
    }

    /// <summary>
    /// What prdb answered with when it answered at all.
    /// </summary>
    /// <remarks>
    /// ADR 0064's split, and the status code is the whole of it. <c>400</c>,
    /// <c>403</c>, <c>404</c> and <c>409</c> are prdb having considered the
    /// request and declined it: final, recorded, never regenerated and never
    /// resubmitted — and a <c>409</c> is read as <em>this already exists</em>
    /// rather than as a collision, which is the safe reading whichever it
    /// means. A <c>401</c> is not about this submission at all, so nothing is
    /// settled from it; the governor has already stopped every routine over it.
    /// Everything else prdb can answer is a server saying it may or may not
    /// have done the work, which is the uncertain case.
    /// </remarks>
    private async Task<RunResult> AnsweredAsync(
        PreviewPublicationRow owed,
        ApiException refused,
        int settled,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (refused.ResponseStatusCode)
        {
            case 400 or 403 or 404 or 409:
                owed.State = PreviewPublicationState.Refused;
                owed.Note = refused.ResponseStatusCode == 409
                    ? "prdb answered 409: a preview of this file has already been submitted."
                    : $"prdb declined the upload with {refused.ResponseStatusCode}.";
                owed.SettledAt = now;

                // Final. Nothing about these bytes will be sent again, and
                // regenerating them would produce the same picture prdb has
                // just declined.
                store.Delete(owed.Id);

                await context.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "prdb declined a generated preview with {Status}. It is not sent again.",
                    refused.ResponseStatusCode);

                return RunResult.Handled(settled + 1, owed.Note);

            case 401 or 429 or 503:
                // Nothing was accepted and nothing was considered: a key that
                // no longer works, or a service saying not now. The row goes
                // back untouched and the governor decides when anything is
                // tried again.
                owed.State = PreviewPublicationState.Ready;

                await context.SaveChangesAsync(cancellationToken);

                // No wait of its own: a 429 has already shut the budget for as
                // long as prdb asked for, and a 401 has stopped every routine
                // until a key works. The lane's own backoff covers the rest.
                return RunResult.Failed($"prdb answered the upload with {refused.ResponseStatusCode}.");

            default:
                return await UncertainAsync(
                    owed,
                    $"prdb answered {refused.ResponseStatusCode}, which says nothing about whether "
                    + "the upload was accepted.",
                    settled,
                    now,
                    cancellationToken);
        }
    }

    /// <summary>
    /// The outcome nothing is retried from.
    /// </summary>
    private async Task<RunResult> UncertainAsync(
        PreviewPublicationRow owed,
        string why,
        int settled,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        owed.State = PreviewPublicationState.Uncertain;
        owed.Note = why;
        owed.SettledAt = now;

        // The bytes stay. A person may decide to send them, and a decision
        // without them would be a decision to regenerate first.
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "A generated preview was submitted and its outcome is unknown: {Why} "
            + "It is not sent again unless somebody says so.",
            why);

        return RunResult.Failed(why);
    }

    /// <summary>
    /// Turns every row left in flight by a stopped container into the question
    /// it is.
    /// </summary>
    /// <remarks>
    /// Nothing else can write <see cref="PreviewPublicationState.Sending"/> and
    /// leave: the lane is serial, so a row found here at the top of a run
    /// belongs to a request this process did not live to hear the answer to —
    /// or to a Restore of a document written while one was in flight, which is
    /// the same question with the same answer.
    /// </remarks>
    private async Task<int> StrandedAsync(CancellationToken cancellationToken)
    {
        var stranded = await context.PreviewPublications
            .AsTracking()
            .Where(row => row.State == PreviewPublicationState.Sending)
            .Take(ABatch)
            .ToListAsync(cancellationToken);

        if (stranded.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();

        foreach (var row in stranded)
        {
            row.State = PreviewPublicationState.Uncertain;
            row.Note = "The tool stopped while this upload was in flight. Whether prdb accepted "
                + "it cannot be established.";
            row.SettledAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "{Count} upload(s) were in flight when the tool last stopped. Their outcome is unknown.",
            stranded.Count);

        return stranded.Count;
    }

    /// <summary>
    /// Settles an uncertain upload where prdb has since shown the picture it
    /// was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0064's opportunistic resolution, and it is exactly that: the
    /// ordinary per-Video read brings a preview back carrying the osHash it was
    /// made from, and a publicly visible sprite of this installation's own
    /// bytes is proof enough that the submission landed. It depends on
    /// moderation, which promises no timescale, so nothing waits on it and no
    /// behaviour is built around it.
    /// </para>
    /// <para>
    /// It resolves in one direction only. A row that is still absent has told
    /// nobody anything — absence is the ordinary state of everything in
    /// moderation — so this never concludes that an upload failed.
    /// </para>
    /// </remarks>
    private async Task<int> ReconciledAsync(CancellationToken cancellationToken)
    {
        var uncertain = await context.PreviewPublications
            .AsTracking()
            .Where(row => row.State == PreviewPublicationState.Uncertain)
            .OrderBy(row => row.SettledAt)
            .Take(ABatch)
            .ToListAsync(cancellationToken);

        if (uncertain.Count == 0)
        {
            return 0;
        }

        // Every uncertain row at once, because the population is local and one
        // query over the batch costs what one query over a single row does.
        var shown = await publications.ShownSpritesAsync(
            [.. uncertain.Select(row => row.OsHash)],
            cancellationToken);

        var resolved = 0;
        var now = time.GetUtcNow();

        foreach (var row in uncertain)
        {
            if (!shown.TryGetValue(row.OsHash, out var sprite))
            {
                continue;
            }

            row.State = PreviewPublicationState.Sent;
            row.PrdbImageId = sprite.PrdbId;
            row.SubmittedUnder = sprite.Signature;
            row.SettledAt = now;
            row.Note = "prdb now shows a preview made from this file, so the upload did arrive.";

            store.Delete(row.Id);

            resolved++;
        }

        if (resolved > 0)
        {
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "{Count} upload(s) of unknown outcome turned out to have arrived: prdb is showing them.",
                resolved);
        }

        return resolved;
    }

    /// <summary>
    /// Gives up every generated pair that belongs to an account this
    /// installation is no longer signed in as.
    /// </summary>
    /// <remarks>
    /// ADR 0064: changing the prdb key does not carry pending uploads over.
    /// Sending one account's queue under another's key would put somebody
    /// else's picture in this account's gallery, which is the failure the
    /// account being part of this row's identity exists to prevent. What is
    /// <em>not</em> touched is the history — sent, refused and uncertain rows
    /// of any account stay, because they are what stops a file being published
    /// twice.
    /// </remarks>
    private async Task<int> AbandonedAsync(string userHash, CancellationToken cancellationToken)
    {
        var abandoned = await context.PreviewPublications
            .AsTracking()
            .Where(row => row.State == PreviewPublicationState.Ready && row.UserHash != userHash)
            .Take(ABatch)
            .ToListAsync(cancellationToken);

        if (abandoned.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();

        foreach (var row in abandoned)
        {
            row.State = PreviewPublicationState.Dropped;
            row.Note = "The prdb account changed before it was sent.";
            row.SettledAt = now;

            store.Delete(row.Id);
        }

        await context.SaveChangesAsync(cancellationToken);

        return abandoned.Count;
    }

    /// <summary>
    /// Whether this row may still go out, read as late as anything can be.
    /// </summary>
    private async Task<bool> StillSendingAsync(
        string userHash,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var now = await context.Installation
            .AsNoTracking()
            .Select(row => new
            {
                row.PublishGeneratedPreviews,
                row.PreviewPublicationExplainedAt,
                row.PrdbApiKey,
                row.PrdbUserHash,
            })
            .SingleAsync(cancellationToken);

        return now.PublishGeneratedPreviews
            && now.PreviewPublicationExplainedAt is not null
            && now.PrdbUserHash == userHash
            && now.PrdbApiKey == apiKey;
    }

    private static RunResult Done(int settled) =>
        settled > 0 ? RunResult.Handled(settled) : RunResult.NothingToDo;

    /// <summary>
    /// What went wrong, as much of it as belongs in a run log entry a person
    /// reads.
    /// </summary>
    private static string Sentence(Exception failed)
    {
        var said = failed.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = said.Length == 0 ? failed.GetType().Name : said[0];

        return first.Length <= 200 ? first : first[..200];
    }
}
