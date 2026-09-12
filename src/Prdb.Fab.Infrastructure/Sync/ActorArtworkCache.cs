using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Serves an Actor profile image through fab's bounded local cache.</summary>
/// <remarks>
/// The Video side's shape, applied to the one other thing a browse grid draws a
/// picture of. Filling and recording are separated for the reason
/// <see cref="ArtworkCache.FillAsync"/> gives — so that
/// <see cref="ArtworkRoutine"/> can warm several at once over a
/// <c>DbContext</c> that may only be used from one place at a time — and the
/// fetch itself is <see cref="ArtworkCache.FillAsync"/> verbatim, because an
/// Actor's cache key is a <see cref="System.Guid"/> in the same store.
/// </remarks>
public sealed class ActorArtworkCache(
    FabDbContext context,
    ArtworkStore store,
    ArtworkGateway gateway,
    TimeProvider time)
{
    public async Task<ArtworkAnswer> ServeAsync(Guid actorId, CancellationToken cancellationToken)
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
                row.ArtworkLastServedAt,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (actor is null
            || actor.ProfileImageUrl is null
            || actor.ArtworkCacheKey is not { } cacheKey
            || actor.ArtworkFoundDead)
        {
            return ArtworkAnswer.Absent;
        }

        if (!actor.ArtworkCached || !store.Holds(cacheKey))
        {
            var fetch = await gateway.FetchAsync(actor.ProfileImageUrl, cancellationToken);
            if (fetch.Bytes is not null)
            {
                await store.WriteAsync(cacheKey, fetch.Bytes, cancellationToken);
            }
            else if (fetch.UrlIsDead)
            {
                store.Delete(cacheKey);
            }

            await RecordAsync(actor.Id, fetch, cancellationToken);

            if (fetch.Bytes is null)
            {
                return fetch.UrlIsDead ? ArtworkAnswer.Absent : ArtworkAnswer.NotNow;
            }
        }

        var bytes = store.Open(cacheKey);
        if (bytes is null) return ArtworkAnswer.NotNow;

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
            return ArtworkAnswer.NotNow;
        }

        await ServedAsync(actor.Id, actor.ArtworkLastServedAt, cancellationToken);

        return ArtworkAnswer.Of(new Served(bytes, mediaType));
    }

    /// <summary>
    /// Writes what a fetch established about one Actor: cached, dead, or
    /// neither.
    /// </summary>
    /// <remarks>
    /// <see cref="ArtworkCache.RecordAsync"/>'s rule, on the other table. A
    /// transport failure writes nothing: it is not a dead URL, and treating it
    /// as one would turn a flaky minute into a permanently blank Actor.
    /// </remarks>
    public async Task RecordAsync(long actorId, ArtworkFetch fetch, CancellationToken cancellationToken)
    {
        if (fetch.Bytes is not null)
        {
            await context.CatalogueActors
                .Where(row => row.Id == actorId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.ArtworkCached, true)
                    .SetProperty(row => row.ArtworkFoundDead, false),
                    cancellationToken);

            return;
        }

        if (!fetch.UrlIsDead)
        {
            return;
        }

        await context.CatalogueActors
            .Where(row => row.Id == actorId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.ArtworkCached, false)
                .SetProperty(row => row.ArtworkFoundDead, true),
                cancellationToken);
    }

    /// <summary>
    /// The serving stamp, written no more often than
    /// <see cref="ArtworkCache.ServedAgain"/>.
    /// </summary>
    private Task ServedAsync(
        long actorId,
        DateTimeOffset? lastServedAt,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        if (lastServedAt is { } last && now - last < ArtworkCache.ServedAgain)
        {
            return Task.CompletedTask;
        }

        return context.CatalogueActors
            .Where(row => row.Id == actorId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.ArtworkLastServedAt, now),
                cancellationToken);
    }
}
