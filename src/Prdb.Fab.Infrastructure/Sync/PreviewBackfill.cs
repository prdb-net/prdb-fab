using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0064's explicit bounded request: the one way the Library this
/// installation already holds is ever published.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing starts one but a person.</strong> Not starting up, not
/// upgrading, not turning the switch on, and not restoring a Backup — the ADR
/// names the first three and the fourth follows from the same argument, which
/// is why <see cref="PreviewBackfillRow"/> deliberately stays behind at the
/// export boundary. An installation with five thousand entries that upgraded
/// on Tuesday has published nothing on Wednesday unless somebody said so.
/// </para>
/// <para>
/// <strong>A request holds no list.</strong> What is left to take up is a
/// query — an eligible Video File with no publication row for this account and
/// output version — and taking one up writes the row that removes it from that
/// query's answer. So a restart re-asks the question rather than recovering a
/// position, a second request finds only what the first did not reach, and a
/// decode given up on leaves a <c>Dropped</c> row, which answers the question
/// as finally as a submission does.
/// </para>
/// <para>
/// <strong>It respects every gate the automatic path does</strong>, because it
/// does not have its own: it writes the same intent through the same
/// <see cref="PreviewPublications.IntendAsync"/>, which reads the switch and
/// the account, and everything downstream — the hash check against the file on
/// disk, the sheet ceiling, the waiting ceiling, the governor — happens to a
/// row it took up exactly as it happens to one Filing wrote.
/// </para>
/// </remarks>
public sealed class PreviewBackfill(
    FabDbContext context,
    PreviewPublications publications,
    PublicationStore store,
    TimeProvider time)
{
    /// <summary>
    /// How many candidates one take-up may look at before giving the lane its
    /// turn back.
    /// </summary>
    /// <remarks>
    /// A take-up asks for one file and ordinarily gets it from the first
    /// candidate. The batch is what stops a pathological row — an osHash of a
    /// shape <see cref="UserPreviewHash.Normalise"/> refuses, which nothing
    /// this tool computes can be — from turning a run into a scan of the whole
    /// Library.
    /// </remarks>
    private const int ABatch = 100;

    /// <summary>
    /// What a person is shown before they ask for anything: how many files it
    /// would be, and whether asking is possible at all.
    /// </summary>
    public async Task<PreviewBackfillOffer> OfferAsync(CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation
            .AsNoTracking()
            .Select(row => new
            {
                row.PrdbUserHash,
                row.PublishGeneratedPreviews,
                row.PreviewPublicationExplainedAt,
            })
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(installation.PrdbUserHash))
        {
            return new PreviewBackfillOffer(
                Eligible: 0,
                Publishing: installation.PublishGeneratedPreviews,
                Explained: installation.PreviewPublicationExplainedAt is not null,
                Connected: false);
        }

        return new PreviewBackfillOffer(
            await EligibleCountAsync(installation.PrdbUserHash, cancellationToken),
            installation.PublishGeneratedPreviews,
            installation.PreviewPublicationExplainedAt is not null,
            Connected: true);
    }

    /// <summary>
    /// Records the request, and refuses where one cannot be honoured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three refusals are the three gates read in the order a person would
    /// ask about them, and none of them is a failure: a request made while the
    /// channel is off, or before the explanation has been read, would be a
    /// request to publish under conditions ADR 0064 says publish nothing. The
    /// form does not offer the button in those states; this refuses anyway,
    /// because a route is reachable without the form.
    /// </para>
    /// <para>
    /// <strong>One request at a time.</strong> A second alongside the first
    /// would select the same files, and the two would then disagree about what
    /// cancelling means. Asking again while one runs answers with the one that
    /// runs.
    /// </para>
    /// </remarks>
    public async Task<PreviewBackfillVerdict> AskAsync(CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation
            .AsNoTracking()
            .Select(row => new
            {
                row.PrdbUserHash,
                row.PublishGeneratedPreviews,
                row.PreviewPublicationExplainedAt,
            })
            .SingleAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(installation.PrdbUserHash))
        {
            return PreviewBackfillVerdict.Refused(
                "There is no prdb account to publish under.");
        }

        if (!installation.PublishGeneratedPreviews)
        {
            return PreviewBackfillVerdict.Refused(
                "Publishing generated previews is switched off.");
        }

        if (installation.PreviewPublicationExplainedAt is null)
        {
            return PreviewBackfillVerdict.Refused(
                "What publishing sends has not been read yet.");
        }

        if (await OpenAsync(cancellationToken) is not null)
        {
            return PreviewBackfillVerdict.Refused(
                "A request for the existing Library is already under way.");
        }

        var now = time.GetUtcNow();

        var row = new PreviewBackfillRow
        {
            Id = Guid.CreateVersion7(now),
            UserHash = installation.PrdbUserHash,
            State = PreviewBackfillState.Running,
            Selected = await EligibleCountAsync(installation.PrdbUserHash, cancellationToken),
            RequestedAt = now,
        };

        context.PreviewBackfills.Add(row);

        await context.SaveChangesAsync(cancellationToken);

        return PreviewBackfillVerdict.Asked(row.Id);
    }

    /// <summary>Holds back a running request without giving anything up.</summary>
    public Task<bool> PauseAsync(Guid id, CancellationToken cancellationToken = default) =>
        MoveAsync(
            id,
            PreviewBackfillState.Running,
            PreviewBackfillState.Paused,
            "Paused. Nothing new is taken up and what is already generated waits.",
            settled: false,
            cancellationToken);

    /// <summary>Lets a paused request go on from where it stopped.</summary>
    public Task<bool> ResumeAsync(Guid id, CancellationToken cancellationToken = default) =>
        MoveAsync(
            id,
            PreviewBackfillState.Paused,
            PreviewBackfillState.Running,
            note: null,
            settled: false,
            cancellationToken);

    /// <summary>
    /// Ends a request, giving up everything it selected that has not left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What cancelling can reach is an intent and a generated pair waiting for
    /// its upload: both are dropped and the bytes released. What it cannot
    /// reach is anything prdb has — a submission it accepted stays accepted,
    /// because this tool has no retraction and ADR 0064 says so in those words
    /// — and it deliberately leaves an uncertain upload alone, because
    /// cancelling a request is not an answer to a question about one.
    /// </para>
    /// <para>
    /// A paused request may be cancelled as well as a running one, which is the
    /// only reason this takes two states rather than one: pausing to think is
    /// the ordinary way somebody arrives at cancelling.
    /// </para>
    /// </remarks>
    public async Task<PreviewBackfillCancellation?> CancelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await context.PreviewBackfills
            .AsTracking()
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

        if (row is null
            || row.State is not (PreviewBackfillState.Running or PreviewBackfillState.Paused))
        {
            return null;
        }

        var now = time.GetUtcNow();

        var unsent = await context.PreviewPublications
            .AsTracking()
            .Where(publication => publication.BackfillId == id
                                  && (publication.State == PreviewPublicationState.Intended
                                      || publication.State == PreviewPublicationState.Ready))
            .ToListAsync(cancellationToken);

        foreach (var publication in unsent)
        {
            publication.State = PreviewPublicationState.Dropped;
            publication.Note = "The Library request that selected it was cancelled.";
            publication.SettledAt = now;

            store.Delete(publication.Id);
        }

        var sent = await context.PreviewPublications.CountAsync(
            publication => publication.BackfillId == id
                           && publication.State == PreviewPublicationState.Sent,
            cancellationToken);

        row.State = PreviewBackfillState.Cancelled;
        row.SettledAt = now;
        row.Note = sent == 0
            ? "Cancelled before anything of it was accepted by prdb."
            : $"Cancelled. {sent} preview(s) prdb had already accepted stay at prdb: "
              + "there is no retraction for one.";

        await context.SaveChangesAsync(cancellationToken);

        return new PreviewBackfillCancellation(unsent.Count, sent);
    }

    /// <summary>
    /// The request that is still taking files up, or holding them, or null
    /// where nobody has asked.
    /// </summary>
    public Task<PreviewBackfillRow?> OpenAsync(CancellationToken cancellationToken = default) =>
        context.PreviewBackfills
            .AsNoTracking()
            .Where(row => row.State == PreviewBackfillState.Running
                          || row.State == PreviewBackfillState.Paused)
            .OrderByDescending(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Takes up the next eligible Library file, or reports that there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the generating routine and only when nothing Filing wrote is
    /// waiting, which is what keeps ADR 0064's automatic scope in front of a
    /// backlog somebody asked for. One file per call, because the run that
    /// follows it decodes that file.
    /// </para>
    /// <para>
    /// Finding nothing left is what finishes a request, and it is the only way
    /// one finishes on its own. It says nothing about the uploads behind it:
    /// the last file taken up may still be generating when the request that
    /// took it up reads <c>Finished</c>.
    /// </para>
    /// </remarks>
    public async Task<bool> TakeUpAsync(string userHash, CancellationToken cancellationToken = default)
    {
        var request = await context.PreviewBackfills
            .AsTracking()
            .Where(row => row.State == PreviewBackfillState.Running && row.UserHash == userHash)
            .OrderBy(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return false;
        }

        var candidates = await EligibleAsync(userHash, ABatch, cancellationToken);

        foreach (var candidate in candidates)
        {
            if (await publications.IntendAsync(
                    candidate.Id,
                    candidate.LibraryEntryVideoId,
                    candidate.OsHash,
                    candidate.RuntimeSeconds,
                    cancellationToken) is not { } intent)
            {
                continue;
            }

            // The intent is this request's, which is what a pause holds back
            // and a cancellation gives up. Filing's own intents carry no
            // request and are never either.
            intent.BackfillId = request.Id;

            request.TakenUp++;

            await context.SaveChangesAsync(cancellationToken);

            return true;
        }

        // Nothing the query offered could be taken up, which for a batch this
        // size means there is nothing left. A file whose hash the normaliser
        // refuses would read the same way, and would be one nothing here can
        // publish either.
        request.State = PreviewBackfillState.Finished;
        request.SettledAt = time.GetUtcNow();
        request.Note = request.TakenUp == 0
            ? "Every eligible Library file had already been published or given up on."
            : $"Every eligible Library file was taken up: {request.TakenUp} in all.";

        await context.SaveChangesAsync(cancellationToken);

        return false;
    }

    /// <summary>
    /// Gives up a request made under an account this installation is no longer
    /// signed in as.
    /// </summary>
    /// <remarks>
    /// ADR 0064's account rule applied to the request rather than to the rows:
    /// a backfill is a decision to put this Library's pictures in one account's
    /// public gallery, and the new account never asked for that. The rows it
    /// selected are given up by the routines' own account passes.
    /// </remarks>
    public async Task<int> AbandonAsync(string userHash, CancellationToken cancellationToken = default)
    {
        var abandoned = await context.PreviewBackfills
            .AsTracking()
            .Where(row => (row.State == PreviewBackfillState.Running
                           || row.State == PreviewBackfillState.Paused)
                          && row.UserHash != userHash)
            .ToListAsync(cancellationToken);

        if (abandoned.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow();

        foreach (var row in abandoned)
        {
            row.State = PreviewBackfillState.Cancelled;
            row.SettledAt = now;
            row.Note = "The prdb account changed, and the new one never asked for this.";
        }

        await context.SaveChangesAsync(cancellationToken);

        return abandoned.Count;
    }

    /// <summary>How many eligible Library files are still to be taken up.</summary>
    public async Task<int> EligibleCountAsync(string userHash, CancellationToken cancellationToken) =>
        await Eligible(userHash).CountAsync(cancellationToken);

    private async Task<IReadOnlyList<VideoFileRow>> EligibleAsync(
        string userHash,
        int most,
        CancellationToken cancellationToken) =>
        await Eligible(userHash)
            .OrderBy(file => file.Id)
            .Take(most)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Every Video File of the existing Library that could still become a
    /// publication under this account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The eligibility is ADR 0064's, read off the Library rather than off an
    /// arrival: a Video File belongs to a Library Entry and therefore to a prdb
    /// Video, so what is left to check is the osHash the Probe recorded and the
    /// Runtime the interval is computed from. An unidentified file is not in
    /// this table at all, which is why nothing here has to exclude one.
    /// </para>
    /// <para>
    /// <strong>The two spellings are why the hash is upper-cased here.</strong>
    /// A file's osHash is written as <c>Prdb.Hashing</c> computes it, in lower
    /// case; a publication's is written as <see cref="UserPreviewHash"/>
    /// compares by, in upper. Comparing the columns as they stand would match
    /// nothing, and this query would then offer every file that had already
    /// been published — each of which <see cref="PreviewPublications"/> would
    /// refuse, leaving a request that took nothing up and never finished.
    /// </para>
    /// </remarks>
    private IQueryable<VideoFileRow> Eligible(string userHash) =>
        context.VideoFiles
            .AsNoTracking()
            .Where(file => file.OsHash != null
                           && file.OsHash.Length == UserPreviewHash.Length
                           && file.RuntimeSeconds > 0
                           && !context.PreviewPublications.Any(
                               row => row.UserHash == userHash
                                      && row.OsHash == file.OsHash.ToUpper()
                                      && row.OutputVersion == PreviewPublicationContract.OutputVersion));

    private async Task<bool> MoveAsync(
        Guid id,
        PreviewBackfillState from,
        PreviewBackfillState to,
        string? note,
        bool settled,
        CancellationToken cancellationToken)
    {
        var row = await context.PreviewBackfills
            .AsTracking()
            .SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

        if (row is null || row.State != from)
        {
            return false;
        }

        row.State = to;
        row.Note = note;
        row.SettledAt = settled ? time.GetUtcNow() : null;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

/// <summary>
/// What is shown before anybody asks for the existing Library, which ADR 0064
/// requires be the count and the explanation rather than a button alone.
/// </summary>
public sealed record PreviewBackfillOffer(int Eligible, bool Publishing, bool Explained, bool Connected)
{
    /// <summary>Whether asking would be honoured.</summary>
    public bool Askable => Connected && Publishing && Explained && Eligible > 0;
}

/// <summary>The request, or the sentence saying why there is none.</summary>
public sealed record PreviewBackfillVerdict(Guid? Id, string? Refusal)
{
    public static PreviewBackfillVerdict Asked(Guid id) => new(id, Refusal: null);

    public static PreviewBackfillVerdict Refused(string why) => new(Id: null, why);
}

/// <summary>What a cancellation gave up, and what it could not.</summary>
public sealed record PreviewBackfillCancellation(int Dropped, int Accepted);
