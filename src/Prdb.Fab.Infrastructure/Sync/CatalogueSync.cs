using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Small local projections of the feeds behind each browse surface.</summary>
public sealed class CatalogueSync(FabDbContext context, TimeProvider time)
{
    public async Task<IReadOnlyList<CatalogueSyncState>> ReadAsync(CancellationToken cancellationToken)
    {
        var routines = await context.Routines.Where(row => row.Target == null).ToListAsync(cancellationToken);
        var cursors = await context.FeedCursors.ToDictionaryAsync(row => row.Feed, cancellationToken);
        var now = time.GetUtcNow();
        return new[] { "wanted", "actors", "sites" }.Select(surface =>
        {
            var names = RoutineNames(surface, routines.Any(row => row.Name == ActorDrainRoutine.RoutineName));
            var rows = routines.Where(row => names.Contains(row.Name)).ToArray();
            var feeds = FeedsFor(surface).Select(feed => cursors.GetValueOrDefault(feed)).ToArray();
            var lastSynced = feeds.All(row => row?.LastSyncedAt is not null)
                ? feeds.Min(row => row!.LastSyncedAt) : null;
            var deferred = rows.FirstOrDefault(row => row.DeferredUntil > now);
            var failed = rows.Any(row => row.ConsecutiveFailures > 0);
            var pending = rows.Any(row => row.RunNowPending);
            var current = rows.Length == names.Length && lastSynced is not null
                && feeds.All(row => !row!.HasMore) && rows.All(row => row.DueAt > now)
                && !failed && !pending && deferred is null;
            var state = deferred is not null ? CatalogueSyncOutcome.Deferred
                : failed ? CatalogueSyncOutcome.Failed
                : pending ? CatalogueSyncOutcome.Scheduled
                : current ? CatalogueSyncOutcome.Current
                : CatalogueSyncOutcome.Pending;
            return new CatalogueSyncState(surface, state, lastSynced,
                deferred?.LastDeferredReason, deferred?.DeferredUntil);
        }).ToArray();
    }

    public async Task<IReadOnlyList<string>> RoutineNamesAsync(string surface, CancellationToken cancellationToken) =>
        RoutineNames(surface, await context.Routines.AnyAsync(row => row.Name == ActorDrainRoutine.RoutineName, cancellationToken));

    private static string[] RoutineNames(string surface, bool draining) => surface switch
    {
        "wanted" => [WantedVideoFeedRoutine.RoutineName],
        "actors" => [draining ? ActorDrainRoutine.RoutineName : ActorFeedRoutine.RoutineName, FavouriteActorFeedRoutine.RoutineName],
        "sites" => [SiteFeedRoutine.RoutineName, FavouriteSiteFeedRoutine.RoutineName, IgnoredSiteFeedRoutine.RoutineName],
        _ => [],
    };

    private static Feed[] FeedsFor(string surface) => surface switch
    {
        "wanted" => [Feed.WantedVideos],
        "actors" => [Feed.Actors, Feed.FavouriteActors],
        "sites" => [Feed.SiteChanges, Feed.FavouriteSites, Feed.IgnoredSites],
        _ => [],
    };
}

public enum CatalogueSyncOutcome { Pending, Scheduled, Current, Deferred, Failed }
public sealed record CatalogueSyncState(string Surface, CatalogueSyncOutcome Outcome, DateTimeOffset? LastSyncedAt,
    string? Detail, DateTimeOffset? RetryAt);
