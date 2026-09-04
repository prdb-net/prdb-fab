using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// <c>GET /actors/changes</c>: the people credited on videos, whole.
/// </summary>
/// <remarks>
/// The one feed ADR 0013 keeps in full rather than as a fraction. A catalogue
/// video references actors instead of copying them precisely because this feed
/// holds them, so it is not a cache of what has been looked at — it is the table
/// the credits point into, and it is drained once at the start.
/// </remarks>
public sealed class ActorFeed(
    FabDbContext context,
    PrdbGateway prdb,
    CatalogueRows catalogue,
    ActorDetails details)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.Actors;

    public override PrdbWork Work => PrdbWork.Actors;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(
            apiKey,
            Work,
            (client, token) => client.Actors.Changes.GetAsync(
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

    private Task<int> ApplyAsync(List<ActorChangeDto>? items, CancellationToken cancellationToken)
    {
        var actors = (items ?? [])
            .Select(change => change.Actor)
            .Where(actor => actor?.Id is not null)
            .Select(actor => actor!)
            .ToList();
        return details.WriteChangesAsync(actors, cancellationToken);
    }

}
