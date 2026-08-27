using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// <c>GET /favorite-actors/changes</c>: the actors the user follows in prdb.
/// </summary>
/// <remarks>
/// Account-scoped. The actor it names is joined to the copy the actors feed
/// holds rather than built from this payload where one already exists: this
/// feed types gender, nationality and ethnicity as plain label strings where the
/// actor feed types them as enumerations, so the richer copy is the one worth
/// keeping.
/// </remarks>
public sealed class FavouriteActorFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.FavouriteActors;

    public override PrdbWork Work => PrdbWork.UserFeeds;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition? from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.FavoriteActors.Changes.GetAsync(
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

    private async Task<int> ApplyAsync(List<FavoriteActorChangeDto>? items, CancellationToken cancellationToken)
    {
        var favourites = (items ?? [])
            .Select(change => change.FavoriteActor)
            .Where(actor => actor?.Id is not null)
            .ToList();

        var applied = 0;

        foreach (var favourite in favourites)
        {
            var prdbId = favourite!.Id!.Value;

            if (favourite.IsDeleted is true)
            {
                if (await Catalogue.FindActorAsync(prdbId, cancellationToken) is { } gone)
                {
                    await Context.FavouriteActors
                        .Where(row => row.ActorId == gone)
                        .ExecuteDeleteAsync(cancellationToken);
                }

                applied++;
                continue;
            }

            var actorId = await Catalogue.ActorAsync(prdbId, favourite.Name, cancellationToken);

            var held = await Context.FavouriteActors
                .AsTracking()
                .SingleOrDefaultAsync(row => row.ActorId == actorId, cancellationToken);

            var since = favourite.FavoritedAtUtc ?? favourite.UpdatedAtUtc ?? default;

            if (held is null)
            {
                Context.FavouriteActors.Add(new FavouriteActorRow { ActorId = actorId, SinceAt = since });
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
