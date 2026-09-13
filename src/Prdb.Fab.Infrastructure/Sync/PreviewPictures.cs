using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The one prdb request a Preview may spend (ADR 0060): a detail read for a
/// Video the Catalogue holds no picture of.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the gap is there at all.</strong> <c>GET /videos</c> sends no
/// images — that is why <see cref="VideoImageFeed"/> exists — so a row written
/// from the list feed arrives with none and acquires them when the feed or a
/// repair read reaches it. It closes on its own; it does not close while
/// somebody is looking at the sheet.
/// </para>
/// <para>
/// <strong>What makes it at most once.</strong> Nothing new is recorded.
/// <see cref="CatalogueVideoRow.LastReadAt"/> already says when this Video was
/// last read from prdb in detail, and <see cref="VideoDetails"/> writes it —
/// so *read recently and still no pictures* means prdb publishes none, and
/// asking again would learn nothing. ADR 0033's argument against a stored fact
/// with two writers and no reader applies exactly: a second column saying
/// *asked* would be that.
/// </para>
/// <para>
/// While the read is pending the routine row is the record, which is why
/// pressing the same sheet open a hundred times schedules once. A row created
/// at runtime needs nothing registered for it — only a name that already
/// exists (ADR 0038).
/// </para>
/// </remarks>
public sealed class PreviewPictures(FabDbContext context, TimeProvider time)
{
    /// <summary>
    /// How long a Video that was read in detail is taken at its word when it
    /// says prdb publishes no picture of it.
    /// </summary>
    /// <remarks>
    /// A week. A Video that genuinely has none is the normal case and must not
    /// cost a request every time somebody opens it; a Video that acquires one
    /// later should not need a person to notice, and the image feed is what
    /// normally brings it. This is the backstop for the Video the feed's cursor
    /// was already past when the row was written.
    /// </remarks>
    public static readonly TimeSpan Recently = TimeSpan.FromDays(7);

    public async Task<PreviewPictureAsk> AskAsync(Guid videoPrdbId, CancellationToken cancellationToken)
    {
        var video = await context.CatalogueVideos
            .Where(row => row.PrdbId == videoPrdbId)
            .Select(row => new { row.Id, row.LastReadAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (video is null)
        {
            return new(PreviewPictureOutcome.VideoNotFound);
        }

        if (await context.CatalogueImages.AnyAsync(row => row.VideoId == video.Id, cancellationToken))
        {
            // There is a gallery already. Whether its pictures are the newest
            // prdb has is the repair pass's question, not a Preview's.
            return new(PreviewPictureOutcome.AlreadyKnown);
        }

        if (video.LastReadAt > time.GetUtcNow() - Recently)
        {
            return new(PreviewPictureOutcome.PrdbPublishesNone);
        }

        var target = Target(videoPrdbId);
        var pending = await context.Routines
            .AnyAsync(row => row.Name == PreviewPictureRoutine.RoutineName && row.Target == target,
                cancellationToken);

        if (pending)
        {
            return new(PreviewPictureOutcome.Asked);
        }

        context.Routines.Add(new RoutineRow
        {
            Name = PreviewPictureRoutine.RoutineName,
            Target = target,
            Lane = PreviewPictureRoutine.ItsLane,
            DueAt = time.GetUtcNow(),
        });

        await context.SaveChangesAsync(cancellationToken);

        return new(PreviewPictureOutcome.Asked);
    }

    internal static string Target(Guid videoPrdbId) => videoPrdbId.ToString("D");
}

/// <summary>What became of a Preview's ask.</summary>
public enum PreviewPictureOutcome
{
    /// <summary>A read is scheduled, or one already was.</summary>
    Asked,

    /// <summary>The Catalogue already holds pictures of this Video.</summary>
    AlreadyKnown,

    /// <summary>
    /// It was read from prdb recently enough that its having no pictures is
    /// prdb's answer rather than this installation's ignorance.
    /// </summary>
    PrdbPublishesNone,

    /// <summary>The Catalogue does not hold the Video at all.</summary>
    VideoNotFound,
}

/// <summary>ADR 0040's verdict: a refusal is an outcome rather than an error.</summary>
public sealed record PreviewPictureAsk(PreviewPictureOutcome Outcome);
