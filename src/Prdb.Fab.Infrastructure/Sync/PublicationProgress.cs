using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Where every generated preview stands, as one page rather than as a log
/// somebody has to read (ADR 0064, ADR 0018).
/// </summary>
/// <remarks>
/// <para>
/// ADR 0064 left this ticket two things by name and this is where they meet.
/// The first is the Library backfill, which needs somewhere to show its
/// progress and its controls. The second is the uncertain upload: an
/// interrupted submission is recorded and never sent again, because prdb
/// exposes no idempotency key and a row in moderation is invisible to every
/// read endpoint — so sending it again is a person's act, and until now there
/// was nowhere for a person to act.
/// </para>
/// <para>
/// <strong>The words are kept apart here as carefully as in the ADR.</strong>
/// A submission prdb accepted is <em>submitted</em> and waiting for moderation,
/// never <em>published</em>, until a read endpoint has returned the row —
/// which is what the delivering routine's opportunistic resolution watches for
/// and what <see cref="PublicationTally.Shown"/> counts.
/// </para>
/// </remarks>
public sealed class PublicationProgress(
    FabDbContext context,
    PreviewBackfill backfill,
    PublicationStore store,
    TimeProvider time)
{
    /// <summary>
    /// How many uncertain uploads one page shows before it stops naming them
    /// individually.
    /// </summary>
    /// <remarks>
    /// Each one is a decision somebody takes on its own, so they are listed
    /// rather than counted; the bound is what keeps a page from being a table
    /// of two hundred identical questions after a long outage. The tally above
    /// the list is the whole number either way.
    /// </remarks>
    private const int AList = 50;

    public async Task<PublicationProgressState> ReadAsync(CancellationToken cancellationToken = default)
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

        var counts = (await context.PreviewPublications
                .GroupBy(row => row.State)
                .Select(group => new { State = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(item => item.State, item => item.Count);

        var shown = await context.PreviewPublications
            .CountAsync(
                row => row.State == PreviewPublicationState.Sent && row.PrdbImageId != null
                       && context.UserPreviews.Any(preview => preview.PrdbId == row.PrdbImageId
                                                              && preview.Shown
                                                              && !preview.Deleted),
                cancellationToken);

        // Whether the bytes are there is a question for the disk rather than
        // for the database, so the rows are read first and asked afterwards.
        var uncertain = (await context.PreviewPublications
                .AsNoTracking()
                .Where(row => row.State == PreviewPublicationState.Uncertain)
                .OrderByDescending(row => row.SettledAt)
                .Take(AList)
                .Select(row => new
                {
                    row.Id,
                    row.VideoPrdbId,
                    row.OsHash,
                    row.SettledAt,
                    row.Note,
                })
                .ToListAsync(cancellationToken))
            .Select(row => new UncertainPublication(
                row.Id,
                row.VideoPrdbId,
                row.OsHash,
                row.SettledAt,
                row.Note,
                store.Holds(row.Id)))
            .ToList();

        var request = await backfill.OpenAsync(cancellationToken)
            ?? await context.PreviewBackfills
                .AsNoTracking()
                .OrderByDescending(row => row.RequestedAt)
                .FirstOrDefaultAsync(cancellationToken);

        // The offer's own count is what a running request still has in front of
        // it: both are the eligible files of this account, and asking the
        // Library twice for one page would be two scans to say one number.
        var offer = await backfill.OfferAsync(cancellationToken);

        return new PublicationProgressState(
            installation.PublishGeneratedPreviews,
            installation.PreviewPublicationExplainedAt is not null,
            !string.IsNullOrWhiteSpace(installation.PrdbUserHash),
            new PublicationTally(
                counts.GetValueOrDefault(PreviewPublicationState.Intended),
                counts.GetValueOrDefault(PreviewPublicationState.Ready),
                counts.GetValueOrDefault(PreviewPublicationState.Sending),
                counts.GetValueOrDefault(PreviewPublicationState.Sent),
                shown,
                counts.GetValueOrDefault(PreviewPublicationState.Refused),
                counts.GetValueOrDefault(PreviewPublicationState.Uncertain),
                counts.GetValueOrDefault(PreviewPublicationState.Dropped)),
            uncertain,
            request is null
                ? null
                : new PublicationRequest(
                    request.Id,
                    request.State,
                    request.Selected,
                    request.TakenUp,
                    request.RequestedAt,
                    request.SettledAt,
                    request.Note,
                    request.State is PreviewBackfillState.Running or PreviewBackfillState.Paused
                        ? offer.Eligible
                        : 0),
            offer);
    }

    /// <summary>
    /// Sends an uncertain upload again, knowing what it risks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one act in this tool that deliberately risks a duplicate, and it is
    /// a person's because ADR 0064 weighs it and refuses to let the tool take
    /// it: the API offers no idempotency key and no retraction, so a second
    /// copy is a second picture in a public gallery that nobody can take down.
    /// What is here is the capability, not a judgement — the form says what it
    /// costs and this does what it is told.
    /// </para>
    /// <para>
    /// It goes back to <c>Ready</c>, which is the state the delivering routine
    /// takes its work from, so the upload goes out under the ordinary governor
    /// and the ordinary late reads of the switch and the account. Where the
    /// bytes are no longer on disk — a Restore, or a sweep after a long
    /// absence — that routine already knows to put the row back to its intent
    /// and have it made again, which is the same picture from the same file at
    /// the same output version.
    /// </para>
    /// </remarks>
    public async Task<bool> SendAgainAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await Uncertain(id, cancellationToken);

        if (row is null)
        {
            return false;
        }

        row.State = PreviewPublicationState.Ready;
        row.Note = "Asked for again by hand, knowing prdb cannot be asked whether the first one arrived.";
        row.SettledAt = null;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Settles an uncertain upload the other way: it is left as it lies.
    /// </summary>
    /// <remarks>
    /// The decision that the picture is probably at prdb, or that it does not
    /// matter enough to risk a second one. The bytes go, because what they were
    /// kept for was this decision, and the row keeps its hash and its output
    /// version — so the file is never offered again, by a backfill or by
    /// anything else.
    /// </remarks>
    public async Task<bool> LeaveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await Uncertain(id, cancellationToken);

        if (row is null)
        {
            return false;
        }

        row.State = PreviewPublicationState.Dropped;
        row.Note = "Left as it lies by hand: whatever prdb did with it, nothing more is sent.";
        row.SettledAt = time.GetUtcNow();

        store.Delete(row.Id);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private Task<PreviewPublicationRow?> Uncertain(Guid id, CancellationToken cancellationToken) =>
        context.PreviewPublications
            .AsTracking()
            .SingleOrDefaultAsync(
                row => row.Id == id && row.State == PreviewPublicationState.Uncertain,
                cancellationToken);

}

/// <summary>The whole publishing side, as one page reads it.</summary>
public sealed record PublicationProgressState(
    bool Publishing,
    bool Explained,
    bool Connected,
    PublicationTally Tally,
    IReadOnlyList<UncertainPublication> Uncertain,
    PublicationRequest? Request,
    PreviewBackfillOffer Offer);

/// <summary>
/// How many generated previews stand where, which is ADR 0064's states counted.
/// </summary>
/// <param name="Waiting">Owed a decode.</param>
/// <param name="Ready">Decoded and waiting for the wire.</param>
/// <param name="Sending">In flight right now, which is a moment rather than a state to sit in.</param>
/// <param name="Submitted">Accepted by prdb, which is not the same as visible.</param>
/// <param name="Shown">
/// Of those, the ones prdb has since shown publicly — the only evidence
/// moderation let one through.
/// </param>
/// <param name="Refused">Considered by prdb and declined. Final.</param>
/// <param name="Uncertain">Sent, unanswered, and never sent again by the tool.</param>
/// <param name="Dropped">Given up before anything left.</param>
public sealed record PublicationTally(
    int Waiting,
    int Ready,
    int Sending,
    int Submitted,
    int Shown,
    int Refused,
    int Uncertain,
    int Dropped);

/// <summary>
/// One upload whose outcome nobody can establish, as the page needs it.
/// </summary>
/// <param name="Holds">
/// Whether the generated pair is still on disk. False after a Restore, which
/// does not change the decision — it only means the picture is decoded again
/// before it goes.
/// </param>
public sealed record UncertainPublication(
    Guid Id,
    Guid VideoId,
    string OsHash,
    DateTimeOffset? At,
    string? Note,
    bool Holds);

/// <summary>A request for the existing Library, as the page needs it.</summary>
public sealed record PublicationRequest(
    Guid Id,
    PreviewBackfillState State,
    int Selected,
    int TakenUp,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SettledAt,
    string? Note,
    int Remaining);
