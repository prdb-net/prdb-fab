using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// <c>GET /favorite-sites/changes</c>: the sites the user follows in prdb.
/// </summary>
/// <remarks>
/// Account-scoped, like the wanted list beside it. Nothing in this slice shows
/// it — ADR 0012's Sites grid arrives with the action <c>VISION.md</c> attaches
/// to it, which is the matching slice's — so this feed is synced now so that the
/// grid has something to read on the day it is written.
/// </remarks>
public sealed class FavouriteSiteFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.FavouriteSites;

    public override PrdbWork Work => PrdbWork.UserFeeds;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.FavoriteSites.Changes.GetAsync(
                request =>
                {
                    request.QueryParameters.PageSize = pageSize;
                    request.QueryParameters.Since = from.Since;
                    request.QueryParameters.SinceId = from.SinceId;
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

    private async Task<int> ApplyAsync(List<FavoriteSiteChangeDto>? items, CancellationToken cancellationToken)
    {
        var favourites = (items ?? [])
            .Select(change => change.FavoriteSite)
            .Where(site => site?.Id is not null)
            .ToList();

        var applied = 0;

        foreach (var favourite in favourites)
        {
            var prdbId = favourite!.Id!.Value;

            if (favourite.IsDeleted is true)
            {
                // The site row itself is never deleted — ADR 0013 only ever
                // marks one as no longer offered, because a filed path was built
                // from its title. What goes is the following.
                if (await Catalogue.FindSiteAsync(prdbId, cancellationToken) is { } gone)
                {
                    await Context.FavouriteSites
                        .Where(row => row.SiteId == gone)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                applied++;
                continue;
            }

            var siteId = await Catalogue.SiteAsync(
                prdbId,
                favourite.Title,
                favourite.NetworkTitle,
                cancellationToken);

            var held = await Context.FavouriteSites
                .AsTracking()
                .SingleOrDefaultAsync(row => row.SiteId == siteId, cancellationToken);

            var since = favourite.FavoritedAtUtc ?? favourite.UpdatedAtUtc ?? default;

            if (held is null)
            {
                Context.FavouriteSites.Add(new FavouriteSiteRow { SiteId = siteId, SinceAt = since });
            }
            else
            {
                held.SinceAt = since;
            }

            await Context.SaveChangesAsync(cancellationToken);

            applied++;
        }

        return applied;
    }
}
