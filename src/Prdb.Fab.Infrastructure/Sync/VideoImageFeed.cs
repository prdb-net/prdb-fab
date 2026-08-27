using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// <c>GET /videos/images/changes</c>: the artwork that arrives days after the
/// video it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The feed exists because <c>GET /videos</c> carries no image at all, so a
/// consumer paging by <c>createdAtUtc</c> never learns that a video acquired
/// one. That is the whole of its job here, and it is the reason two of its
/// properties are not oversights.
/// </para>
/// <para>
/// <strong>It discards what it cannot place.</strong> The feed is global and the
/// catalogue is a fraction of it, so keeping image rows for videos this
/// installation does not hold would make the image table a multiple of the table
/// it describes (ADR 0013). Nothing is lost by it: no catalogue row arrives
/// without a detail read, and a detail read brings <c>images[]</c> with it.
/// </para>
/// <para>
/// <strong>It never reports a deletion.</strong> Image rows are hard-deleted
/// upstream and simply stop being returned — the API document says so three
/// times, and the payload carries no <c>isDeleted</c> to read. Nothing here
/// tries to notice; the removal is found by ADR 0013's repair pass, diffing the
/// authoritative <c>images[]</c> against what is held.
/// </para>
/// </remarks>
public sealed class VideoImageFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.VideoImages;

    /// <summary>
    /// Alone among the five. See <see cref="ChangeFeedRoutine"/> for why the
    /// history of a global feed is worth nothing to a catalogue that is a
    /// fraction of it.
    /// </summary>
    public override bool StartsAtTheBeginning => false;

    protected override PrdbWork Work => PrdbWork.Images;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition? from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.Videos.Images.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = pageSize;
                    request.QueryParameters.Since = from?.Since;
                    request.QueryParameters.SinceId = from?.SinceId;
                },
                token),
            cancellationToken);

        if (page is null)
        {
            return FeedPage.Nothing;
        }

        return new FeedPage(
            await ApplyAsync(page.Items, cancellationToken),
            page.HasMore ?? false,
            page.NextCursor?.UpdatedAtUtc,
            page.NextCursor?.Id,
            page.ServerTimeUtc);
    }

    private async Task<int> ApplyAsync(List<VideoImageChangeDto>? items, CancellationToken cancellationToken)
    {
        var images = (items ?? [])
            .Select(change => change.Image)
            .Where(image => image?.Id is not null && image.VideoId is not null)
            .ToList();

        if (images.Count == 0)
        {
            return 0;
        }

        var videos = images.Select(image => image!.VideoId!.Value).Distinct().ToList();

        // The one query that decides what this page is worth. Everything naming
        // a video that is not here is dropped, and on a fresh installation that
        // is the whole page.
        var placeable = await Context.CatalogueVideos
            .Where(row => videos.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, row => row.Id, cancellationToken);

        if (placeable.Count == 0)
        {
            return 0;
        }

        var ids = images.Select(image => image!.Id!.Value).ToList();

        var held = await Context.CatalogueImages
            .AsTracking()
            .Where(row => ids.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        var applied = 0;

        foreach (var image in images)
        {
            if (!placeable.TryGetValue(image!.VideoId!.Value, out var videoId))
            {
                continue;
            }

            var id = image.Id!.Value;
            var url = image.Url ?? string.Empty;

            if (!held.TryGetValue(id, out var row))
            {
                row = new CatalogueImageRow { PrdbId = id, VideoId = videoId, Url = url };

                Context.CatalogueImages.Add(row);
                held[id] = row;
            }
            else if (!string.Equals(row.Url, url, StringComparison.Ordinal))
            {
                // ADR 0030 names the cached file by the image id, so a changed
                // URL under one id is the same file name over different bytes —
                // the one case where what is on disk has to be fetched again.
                // A URL that has stopped being dead is worth another look too.
                row.Url = url;
                row.VideoId = videoId;
                row.Cached = false;
                row.FoundDead = false;
            }
            else
            {
                row.VideoId = videoId;
            }

            applied++;
        }

        await Context.SaveChangesAsync(cancellationToken);

        return applied;
    }
}
