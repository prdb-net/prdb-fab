using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Serves an Actor profile image through fab's bounded local cache.</summary>
public sealed class ActorArtworkCache(
    FabDbContext context,
    ArtworkStore store,
    ArtworkGateway gateway,
    TimeProvider time)
{
    public async Task<Served?> ServeAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await context.CatalogueActors
            .AsNoTracking()
            .Where(row => row.PrdbId == actorId)
            .Select(row => new
            {
                row.Id,
                row.ProfileImageUrl,
                row.ArtworkCacheKey,
                row.ArtworkCached,
                row.ArtworkFoundDead,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (actor is null
            || actor.ProfileImageUrl is null
            || actor.ArtworkCacheKey is not { } cacheKey
            || actor.ArtworkFoundDead)
        {
            return null;
        }

        if (!actor.ArtworkCached || !store.Holds(cacheKey))
        {
            var fetch = await gateway.FetchAsync(actor.ProfileImageUrl, cancellationToken);
            if (fetch.Bytes is not null)
            {
                await store.WriteAsync(cacheKey, fetch.Bytes, cancellationToken);
                await context.CatalogueActors
                    .Where(row => row.Id == actor.Id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.ArtworkCached, true)
                        .SetProperty(row => row.ArtworkFoundDead, false),
                        cancellationToken);
            }
            else if (fetch.UrlIsDead)
            {
                store.Delete(cacheKey);
                await context.CatalogueActors
                    .Where(row => row.Id == actor.Id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.ArtworkCached, false)
                        .SetProperty(row => row.ArtworkFoundDead, true),
                        cancellationToken);
                return null;
            }
            else
            {
                return null;
            }
        }

        var bytes = store.Open(cacheKey);
        if (bytes is null) return null;

        var header = new byte[ArtworkFormat.Header];
        var read = await bytes.ReadAtLeastAsync(
            header,
            header.Length,
            throwOnEndOfStream: false,
            cancellationToken);
        bytes.Position = 0;
        var mediaType = ArtworkFormat.MediaTypeOf(header.AsSpan(0, read));
        if (mediaType is null)
        {
            await bytes.DisposeAsync();
            return null;
        }

        await context.CatalogueActors
            .Where(row => row.Id == actor.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.ArtworkLastServedAt, time.GetUtcNow()),
                cancellationToken);
        return new Served(bytes, mediaType);
    }
}
