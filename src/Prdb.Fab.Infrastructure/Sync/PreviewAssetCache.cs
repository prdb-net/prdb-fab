using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0061's asset cache, from the outside: the bytes of one user preview,
/// fetched and validated as a pair if they are not there yet.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A version is committed only when its pair is complete.</strong> Both
/// halves are fetched, both are checked, the WebVTT is parsed against the image
/// the sheet actually is, and only then does <c>CachedVersion</c> move. Until it
/// does, nothing is served: a reader asks for a version by name and the row says
/// which version is whole, so there is no window in which a sprite is served
/// against somebody else's grid.
/// </para>
/// <para>
/// <strong>Two readers of one preview do not fight.</strong> They fetch the same
/// version into the same two file names, each under a temporary name and
/// renamed, and both write the same value to the row. The worst case is one
/// fetch too many, which is the same worst case
/// <see cref="ArtworkCache"/> has and is cheaper than a lock across a request.
/// </para>
/// <para>
/// <strong>Nothing here spends prdb budget.</strong> These are CDN
/// <c>GET</c>s — ADR 0030's argument, unchanged — and the ceilings, the
/// timeouts and the content checks stand in the governor's place.
/// </para>
/// </remarks>
public sealed class PreviewAssetCache(
    FabDbContext context,
    PreviewAssetStore store,
    PreviewAssetGateway gateway,
    TimeProvider time,
    ILogger<PreviewAssetCache> logger)
{
    /// <summary>
    /// How stale <see cref="UserPreviewRow.LastServedAt"/> is allowed to get
    /// before a serve rewrites it. ADR 0059's throttle, for its reason.
    /// </summary>
    public static readonly TimeSpan ServedAgain = TimeSpan.FromHours(1);

    /// <summary>
    /// The bytes of one half of one preview, or why there are none.
    /// </summary>
    /// <param name="previewId">prdb's id for the preview.</param>
    /// <param name="version">
    /// Which version the caller was told about. A request for a version that is
    /// not the current one is answered as absent rather than with the current
    /// one: an address that quietly means something else is the thing versioning
    /// exists to prevent.
    /// </param>
    /// <param name="vtt">The WebVTT rather than the image.</param>
    public async Task<PreviewAssetAnswer> ServeAsync(
        Guid previewId,
        string version,
        bool vtt,
        CancellationToken cancellationToken)
    {
        var row = await context.UserPreviews
            .AsNoTracking()
            .SingleOrDefaultAsync(preview => preview.PrdbId == previewId, cancellationToken);

        // Withdrawn, never held, or a kind this build does not show. All three
        // are the same answer to a browser and none of them is retried.
        if (row is null || !row.Shown || !UserPreviewAsset.IsKnown(row.Kind))
        {
            return PreviewAssetAnswer.Absent;
        }

        if (!string.Equals(version, VersionOf(row), StringComparison.Ordinal))
        {
            return PreviewAssetAnswer.Absent;
        }

        var paired = Paired(row);

        if (vtt && !paired)
        {
            return PreviewAssetAnswer.Absent;
        }

        if (row.CachedVersion != version || !store.Holds(previewId, version, paired))
        {
            if (!await FillAsync(row, version, cancellationToken))
            {
                return PreviewAssetAnswer.NotNow;
            }
        }

        var bytes = vtt ? store.OpenVtt(previewId, version) : store.OpenImage(previewId, version);

        if (bytes is null)
        {
            // The file went between the check and here: a sweep, or somebody
            // with a shell. Nothing is wrong that the next request will not
            // fix, and the row's claim is corrected on the way past.
            await UncacheAsync(row.Id, cancellationToken);

            return PreviewAssetAnswer.NotNow;
        }

        await ServedAsync(row, cancellationToken);

        return PreviewAssetAnswer.Of(new Served(bytes, vtt ? "text/vtt" : "image/jpeg"));
    }

    /// <summary>
    /// Fetches and validates both halves, and commits the version if the whole
    /// asset holds together.
    /// </summary>
    /// <returns>Whether the version is now complete on disk and committed.</returns>
    public async Task<bool> FillAsync(
        UserPreviewRow row,
        string version,
        CancellationToken cancellationToken)
    {
        var paired = Paired(row);

        var image = await gateway.ImageAsync(row.Url, cancellationToken);

        if (image.Bytes is null)
        {
            logger.LogDebug("A user preview image was not fetched: {Reason}", image.Reason);

            return false;
        }

        if (JpegGeometry.Of(image.Bytes) is not { } sheet)
        {
            logger.LogWarning("A user preview image carries no readable geometry and was not kept.");

            return false;
        }

        byte[]? vtt = null;

        if (paired)
        {
            var fetched = await gateway.VttAsync(row.VttUrl, cancellationToken);

            if (fetched.Bytes is null)
            {
                logger.LogDebug("A user preview's WebVTT was not fetched: {Reason}", fetched.Reason);

                return false;
            }

            // Against the sheet the image actually is, rather than against the
            // dimensions its payload claimed. See JpegGeometry.
            var timeline = SpriteTimeline.Read(fetched.Bytes, sheet, UserPreviewContract.Tiles);

            if (!timeline.Usable)
            {
                logger.LogWarning(
                    "A user preview's WebVTT does not describe its sheet and was not kept: {Reason}",
                    timeline.Reason);

                return false;
            }

            vtt = fetched.Bytes;
        }

        await store.WriteImageAsync(row.PrdbId, version, image.Bytes, cancellationToken);

        if (vtt is not null)
        {
            await store.WriteVttAsync(row.PrdbId, version, vtt, cancellationToken);
        }

        // The commit, and the only write that says this version may be served.
        // Both files are on disk before it happens, so a crash before it leaves
        // two files nothing claims — which the sweep takes — rather than a pair
        // half of which is another version.
        await context.UserPreviews
            .Where(preview => preview.Id == row.Id)
            .ExecuteUpdateAsync(
                preview => preview.SetProperty(cached => cached.CachedVersion, version),
                cancellationToken);

        return true;
    }

    /// <summary>
    /// The timeline of a sprite this cache holds, read off the file rather than
    /// off anything stored.
    /// </summary>
    /// <remarks>
    /// What the gallery is given so that it can put a tile under a person's
    /// finger, and what the Library will copy out beside a Video File. The
    /// parse is repeated rather than kept in a column, for ADR 0033's reason:
    /// four hundred cues in a table would be a second copy of a file that is
    /// already on the disk beside it.
    /// </remarks>
    public async Task<SpriteTimelineResult> TimelineAsync(
        Guid previewId,
        string version,
        CancellationToken cancellationToken)
    {
        var image = await store.ReadAsync(store.ImagePathOf(previewId, version), cancellationToken);
        var vtt = await store.ReadAsync(store.VttPathOf(previewId, version), cancellationToken);

        if (image is null || vtt is null)
        {
            return SpriteTimelineResult.Unusable("The pair is not in the cache.");
        }

        return JpegGeometry.Of(image) is { } sheet
            ? SpriteTimeline.Read(vtt, sheet, UserPreviewContract.Tiles)
            : SpriteTimelineResult.Unusable("The sprite sheet has no readable dimensions.");
    }

    /// <summary>Which version of this row's asset is the current one.</summary>
    public static string VersionOf(UserPreviewRow row) => UserPreviewAsset.VersionOf(
        row.Url,
        row.VttUrl,
        row.TileCount,
        row.Columns,
        row.Rows,
        row.TileWidth,
        row.TileHeight);

    /// <summary>
    /// Whether this preview is one whose asset is two files.
    /// </summary>
    /// <remarks>
    /// A sprite sheet with no WebVTT to pair it with is not a sprite sheet this
    /// tool can show — there is nothing to say which tile is when — so it is not
    /// treated as a single picture either. It simply never completes, which is
    /// the same answer as a broken pair and is reached without a special case.
    /// </remarks>
    public static bool Paired(UserPreviewRow row) => UserPreviewAsset.IsSprite(row.Kind);

    private Task UncacheAsync(long id, CancellationToken cancellationToken) =>
        context.UserPreviews
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(
                row => row.SetProperty(preview => preview.CachedVersion, (string?)null),
                cancellationToken);

    private Task ServedAsync(UserPreviewRow row, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        if (row.LastServedAt is { } last && now - last < ServedAgain)
        {
            return Task.CompletedTask;
        }

        return context.UserPreviews
            .Where(preview => preview.Id == row.Id)
            .ExecuteUpdateAsync(
                preview => preview.SetProperty(served => served.LastServedAt, now),
                cancellationToken);
    }
}

/// <summary>
/// What the cache had for one preview: the bytes, or an absence that either
/// stands or is only about this minute.
/// </summary>
/// <remarks>
/// The same two absences <see cref="ArtworkAnswer"/> draws, and here the line
/// between them is drawn differently: <em>stands</em> means prdb is not showing
/// this preview or this is not its current version, which is settled until the
/// change feed says otherwise, and <em>not now</em> means a CDN that did not
/// answer or a pair that did not hold together. Neither is ever permanent, which
/// is why even the settled one is only cacheable for minutes.
/// </remarks>
public sealed record PreviewAssetAnswer(Served? Served, bool AbsenceStands)
{
    /// <summary>Withdrawn, unknown, or a version that is not the current one.</summary>
    public static PreviewAssetAnswer Absent { get; } = new(null, true);

    /// <summary>A CDN that did not answer, or a pair that did not validate.</summary>
    public static PreviewAssetAnswer NotNow { get; } = new(null, false);

    public static PreviewAssetAnswer Of(Served served) => new(served, false);
}
