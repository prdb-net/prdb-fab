using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// What this installation owes prdb in generated previews, and the one door an
/// obligation comes in through (ADR 0064).
/// </summary>
/// <remarks>
/// <para>
/// <strong>One act creates an intent: a Video File being filed.</strong> Not a
/// browse, not a probe, not a restore — ADR 0064 puts the existing Library
/// behind an explicit bounded request precisely so that upgrading does not
/// start a five-thousand-file decode. So this is called from Filing, inside its
/// transaction, for the reason ADR 0061's interest is: a crash between the two
/// would otherwise leave a filed file with nothing owed for it.
/// </para>
/// <para>
/// <strong>The switch is read here and again at the decode.</strong> Here,
/// because a channel that is off should not accumulate work somebody would then
/// have to be told about; there, because it may have been turned off in the
/// hours between. What is <em>not</em> read here is the explanation stamp: the
/// gate ADR 0064 puts on publishing is a Brake on the acting, and an intent
/// waiting behind it is the tool remembering what it will owe once somebody has
/// been asked.
/// </para>
/// </remarks>
public sealed class PreviewPublications(FabDbContext context, TimeProvider time)
{
    /// <summary>
    /// Records that a filed Video File is owed a generated preview, where it is
    /// eligible and nothing is owed for it yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called inside Filing's transaction and saved by it, so nothing here
    /// calls <c>SaveChangesAsync</c>.
    /// </para>
    /// <para>
    /// It answers <see langword="false"/> far more often than it is a failure
    /// for it to: an unidentified file, a file with no Runtime, a channel that
    /// is off, an account that is not configured, and — the ordinary case once
    /// a Library has any size — a file whose exact bytes have been published
    /// already.
    /// </para>
    /// </remarks>
    public async Task<bool> IntendAsync(
        Guid videoFileId,
        Guid videoPrdbId,
        string? osHash,
        long? runtimeSeconds,
        CancellationToken cancellationToken)
    {
        // A file no interval can be computed for is not eligible, and one whose
        // hash the Probe did not record cannot be submitted: the osHash is what
        // ties the picture to the bytes it was made from, and ADR 0064 sends no
        // preview without one.
        if (UserPreviewHash.Normalise(osHash) is not { } hash || runtimeSeconds is not > 0)
        {
            return false;
        }

        var installation = await context.Installation
            .AsNoTracking()
            .Select(row => new { row.PublishGeneratedPreviews, row.PrdbUserHash })
            .SingleAsync(cancellationToken);

        if (!installation.PublishGeneratedPreviews
            || string.IsNullOrWhiteSpace(installation.PrdbUserHash))
        {
            return false;
        }

        // The unique key says the same thing; asking first is what keeps a
        // second filing of the same bytes from failing Filing's transaction
        // over something that is not a problem. A row in any state counts,
        // including a refusal: ADR 0064 does not resubmit what prdb declined.
        if (await context.PreviewPublications.AnyAsync(
                row => row.UserHash == installation.PrdbUserHash
                       && row.OsHash == hash
                       && row.OutputVersion == PreviewPublicationContract.OutputVersion,
                cancellationToken))
        {
            return false;
        }

        var now = time.GetUtcNow();

        context.PreviewPublications.Add(new PreviewPublicationRow
        {
            Id = Guid.CreateVersion7(now),
            VideoFileId = videoFileId,
            VideoPrdbId = videoPrdbId,
            OsHash = hash,
            UserHash = installation.PrdbUserHash,
            OutputVersion = PreviewPublicationContract.OutputVersion,
            State = PreviewPublicationState.Intended,
            IntendedAt = now,
        });

        return true;
    }

    /// <summary>
    /// Whether prdb already shows a Sprite Sheet made from these exact bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0064 publishes what prdb's population is short of, and a file whose
    /// hash already carries a visible sprite is not short of anything. The
    /// answer comes from ADR 0061's population — rows prdb returned from an
    /// endpoint that shows only what moderation has made public — so it is
    /// evidence rather than a guess, and it is why this is asked at the decode
    /// rather than at the intent: the read that fills that population is
    /// scheduled by the same filing and has usually not landed yet.
    /// </para>
    /// <para>
    /// <strong>It is asked of the hash rather than of the Video.</strong>
    /// Somebody else's sprite of a different file of the same scene is a
    /// different picture of different bytes, and ADR 0064's submission is bound
    /// to the file it was made from.
    /// </para>
    /// </remarks>
    public async Task<bool> AlreadyShownAsync(string osHash, CancellationToken cancellationToken)
    {
        var hash = UserPreviewHash.Normalise(osHash);

        // The kind is quoted from prdb rather than parsed (ADR 0061), and its
        // own form says the two words are matched case-insensitively — which
        // SQLite's `=` is not. There are a handful of previews per hash, so the
        // kinds are read and the one comparison this tool has is used on them.
        var kinds = await context.UserPreviews
            .Where(row => row.OsHash == hash
                          && row.Shown
                          && !row.Deleted
                          && row.TileCount >= PreviewPublicationContract.FewestTiles)
            .Select(row => row.Kind)
            .ToListAsync(cancellationToken);

        return kinds.Any(UserPreviewAsset.IsSprite);
    }
}
