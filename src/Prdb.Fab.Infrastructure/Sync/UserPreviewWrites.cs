using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The two writers of <c>user_preview</c>, and the rule that keeps them from
/// undoing each other.
/// </summary>
/// <remarks>
/// <para>
/// A <strong>snapshot</strong> is one Video's list from
/// <c>GET /videos/{id}/user-images</c>, which returns only publicly visible
/// rows — so everything in it is shown, and anything this installation held for
/// that Video and did not get back is not. A <strong>change</strong> is one row
/// off the global feed, which returns everything including what moderation has
/// taken away.
/// </para>
/// <para>
/// <strong>The rule is <c>updatedAtUtc</c>, not arrival.</strong> A snapshot
/// answering after a feed page that already withdrew one of its rows would
/// otherwise restore it, and the two run in different lanes at different
/// cadences, so that race is ordinary rather than exotic. Discarding a payload
/// older than the stored stamp makes the two writers commutative, which is what
/// lets ADR 0013's overlap replay a page without consequence.
/// </para>
/// </remarks>
public sealed class UserPreviewWrites(FabDbContext context, TimeProvider time)
{
    /// <summary>
    /// Applies one Video's list, and takes away what the list no longer
    /// carries.
    /// </summary>
    /// <returns>How many rows the answer was worth.</returns>
    public async Task<int> SnapshotAsync(
        Guid videoPrdbId,
        IReadOnlyList<VideoUserImageDto> answer,
        CancellationToken cancellationToken)
    {
        // prdb's own order — displayOrder, createdAtUtc, id — and then the
        // bound, so that a Video with thousands of previews contributes the
        // sixty a gallery could show rather than all of them.
        var offered = answer
            .Where(row => row.Id is not null)
            .OrderBy(row => row.DisplayOrder ?? 0)
            .ThenBy(row => row.CreatedAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(row => row.Id!.Value)
            .Take(UserPreviewContract.RowsPerVideo)
            .ToList();

        var ids = offered.Select(row => row.Id!.Value).ToList();

        var held = await context.UserPreviews
            .AsTracking()
            .Where(row => row.VideoPrdbId == videoPrdbId || ids.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        var applied = 0;

        foreach (var dto in offered)
        {
            if (Write(dto, held, delivered: true))
            {
                applied++;
            }
        }

        foreach (var stale in held.Values.Where(row =>
                     row.VideoPrdbId == videoPrdbId && !ids.Contains(row.PrdbId)))
        {
            // A filtered list that used to carry this row and no longer does.
            // There is no payload saying why — hidden, denied, deleted or
            // unlinked all look the same from here — so it stops being shown
            // and keeps the signature it was shown under, which is what a
            // later restoration is recognised by.
            Withdraw(stale);
        }

        await context.SaveChangesAsync(cancellationToken);

        return applied;
    }

    /// <summary>
    /// Applies one page of the change feed, ignoring every row nothing here is
    /// interested in.
    /// </summary>
    /// <remarks>
    /// The feed is global and this installation holds a fraction of a percent
    /// of it. A row naming a Video no interest exists for is dropped without a
    /// row and without a byte — the same thing <see cref="VideoImageFeed"/>
    /// does with an image it cannot place, and for the same reason: ADR 0013
    /// refuses a table that is a multiple of the one it describes.
    /// </remarks>
    public async Task<int> ChangesAsync(
        IReadOnlyList<VideoUserImageChangeItemDto> items,
        CancellationToken cancellationToken)
    {
        var rows = items
            .Select(item => item.VideoUserImage)
            .Where(row => row?.Id is not null)
            .Select(row => row!)
            .ToList();

        if (rows.Count == 0)
        {
            return 0;
        }

        var ids = rows.Select(row => row.Id!.Value).ToList();
        var videos = rows.Select(row => row.VideoId).OfType<Guid>().Distinct().ToList();

        var held = await context.UserPreviews
            .AsTracking()
            .Where(row => ids.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        // The one query that decides what this page is worth. A row already
        // held is followed whatever it now says — that is how a withdrawal
        // arrives — and a row nothing holds is worth keeping only where its
        // Video is one this installation is interested in.
        var wanted = await context.UserPreviewInterests
            .Where(row => videos.Contains(row.VideoPrdbId))
            .Select(row => row.VideoPrdbId)
            .ToHashSetAsync(cancellationToken);

        var applied = 0;

        foreach (var dto in rows)
        {
            var known = held.ContainsKey(dto.Id!.Value);
            var interesting = dto.VideoId is { } video && wanted.Contains(video);

            if (!known && !interesting)
            {
                continue;
            }

            if (Write(dto, held, delivered: false))
            {
                applied++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return applied;
    }

    /// <summary>
    /// Takes away every preview of a Video, without deciding whether the rows
    /// go.
    /// </summary>
    /// <remarks>
    /// What an expiring interest leaves behind for one pass, so that anything
    /// serving or reconciling from these rows sees them stop before they
    /// disappear. The rows themselves are dropped by the cleanup that called
    /// this.
    /// </remarks>
    public Task<int> WithdrawVideoAsync(Guid videoPrdbId, CancellationToken cancellationToken) =>
        context.UserPreviews
            .Where(row => row.VideoPrdbId == videoPrdbId && row.Shown)
            .ExecuteUpdateAsync(row => row.SetProperty(preview => preview.Shown, false), cancellationToken);

    /// <summary>
    /// Writes one payload over whatever is held, or refuses it for being older
    /// than what is held.
    /// </summary>
    /// <param name="delivered">
    /// Whether an endpoint that returns only publicly visible rows handed this
    /// over — which is the only thing that establishes a signature to compare
    /// against later.
    /// </param>
    private bool Write(
        VideoUserImageDto dto,
        Dictionary<Guid, UserPreviewRow> held,
        bool delivered)
    {
        var id = dto.Id!.Value;
        var stamp = dto.UpdatedAtUtc ?? time.GetUtcNow();
        var signature = UserPreviewModeration.Signature(dto.ModerationStatus, dto.ModerationVisibility);

        if (UserPreviewHash.Normalise(dto.BasedOnFileWithOsHash) is not { } hash)
        {
            // A preview whose hash is not one. Nothing here can place it
            // against a file, and storing it would mean a column that looks
            // like a hash and matches nothing.
            return false;
        }

        if (!held.TryGetValue(id, out var row))
        {
            row = new UserPreviewRow
            {
                PrdbId = id,
                OsHash = hash,
                Kind = dto.PreviewImageType ?? string.Empty,
                Url = dto.Url ?? string.Empty,
                CreatedAtUtc = dto.CreatedAtUtc ?? stamp,
            };

            context.UserPreviews.Add(row);
            held[id] = row;
        }
        else if (stamp < row.UpdatedAtUtc)
        {
            // Older than what is held. See the class remarks: this is the whole
            // of what makes the snapshot and the feed safe to run at once.
            return false;
        }

        // The pair is one asset. A URL or a geometry that has moved is a
        // different sheet under the same id, so whatever is cached stops being
        // this row's — never half of it.
        if (!string.Equals(row.Url, dto.Url ?? string.Empty, StringComparison.Ordinal)
            || !string.Equals(row.VttUrl, dto.VttUrl, StringComparison.Ordinal)
            || row.TileCount != dto.SpriteTileCount
            || row.Columns != dto.SpriteColumns
            || row.Rows != dto.SpriteRows
            || row.TileWidth != dto.SpriteTileWidth
            || row.TileHeight != dto.SpriteTileHeight)
        {
            row.CachedVersion = null;
        }

        row.VideoPrdbId = dto.VideoId;
        row.OsHash = hash;
        row.Kind = dto.PreviewImageType ?? string.Empty;
        row.Url = dto.Url ?? string.Empty;
        row.VttUrl = dto.VttUrl;
        row.Filesize = dto.Filesize ?? 0;
        row.Width = dto.Width ?? 0;
        row.Height = dto.Height ?? 0;
        row.DisplayOrder = dto.DisplayOrder ?? 0;
        row.TileCount = dto.SpriteTileCount;
        row.TileWidth = dto.SpriteTileWidth;
        row.TileHeight = dto.SpriteTileHeight;
        row.Columns = dto.SpriteColumns;
        row.Rows = dto.SpriteRows;
        row.ModerationStatus = dto.ModerationStatus;
        row.ModerationVisibility = dto.ModerationVisibility;
        row.Deleted = dto.IsDeleted ?? false;
        row.UpdatedAtUtc = stamp;

        if (delivered && !row.Deleted)
        {
            // Visible by construction, whatever it calls itself.
            row.ShownUnder = signature;
            row.Shown = true;
        }
        else if (!UserPreviewModeration.Shows(row.Deleted, signature, row.ShownUnder))
        {
            Withdraw(row);
        }
        else
        {
            row.Shown = true;
        }

        return true;
    }

    /// <summary>
    /// Stops a preview being served and drops its claim on the cache, keeping
    /// the signature that a restoration would be recognised by.
    /// </summary>
    private static void Withdraw(UserPreviewRow row)
    {
        row.Shown = false;

        // The bytes are a copy of something prdb has stopped publishing. The
        // files themselves go with the next sweep, which is what finds a
        // cached version nothing claims.
        row.CachedVersion = null;
    }
}
