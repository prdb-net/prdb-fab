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
public sealed class ActorFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.Actors;

    public override PrdbWork Work => PrdbWork.Actors;

    public override async Task<FeedPage> ReadAsync(
        string apiKey,
        FeedPosition? from,
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

    private async Task<int> ApplyAsync(List<ActorChangeDto>? items, CancellationToken cancellationToken)
    {
        var named = (items ?? [])
            .Select(change => change.Actor)
            .Where(actor => actor?.Id is not null)
            .ToList();

        if (named.Count == 0)
        {
            return 0;
        }

        // One query for the whole page rather than one per row: a drain reads a
        // thousand actors at a time, and this is the difference between one
        // statement and a thousand.
        var ids = named.Select(actor => actor!.Id!.Value).ToList();

        var held = await Context.CatalogueActors
            .AsTracking()
            .Where(row => ids.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, cancellationToken);

        foreach (var actor in named)
        {
            var id = actor!.Id!.Value;
            held.TryGetValue(id, out var row);

            if (actor.IsDeleted is true)
            {
                // prdb soft-deletes an actor and this table has nowhere to say
                // so: ADR 0033 gives the row a name and an id and nothing else.
                // So the row goes, and the credits and the favourite that
                // referenced it go with it — which is what happened upstream.
                if (row is not null)
                {
                    Context.CatalogueActors.Remove(row);
                    held.Remove(id);
                }
            }
            else if (row is null)
            {
                var arrived = new CatalogueActorRow { PrdbId = id, Name = actor.Name ?? string.Empty };

                Context.CatalogueActors.Add(arrived);

                // Kept, because one page can carry the same actor twice: these
                // are current-state feeds, so a row that changed several times
                // since the cursor arrives once — but a row the overlap
                // re-delivers alongside its own later change does not.
                held[id] = arrived;
            }
            else
            {
                // The feed carries the full current row, so an update is the
                // whole of it rather than a diff — and applying it twice is the
                // same as applying it once, which is what the overlap needs.
                row.Name = actor.Name ?? string.Empty;
            }
        }

        await Context.SaveChangesAsync(cancellationToken);

        return named.Count;
    }
}
