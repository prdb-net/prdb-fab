using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class CatalogueSyncTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_partial_or_invalid_page_keeps_the_last_sync_time_without_claiming_to_be_current()
    {
        await using var db = await TestDatabase.CreateAsync(also: services => services.AddFabSync());
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var cursors = scope.ServiceProvider.GetRequiredService<FeedCursors>();
        var now = db.Time.GetUtcNow();
        context.Routines.Add(new() { Name = WantedVideoFeedRoutine.RoutineName, DueAt = now.AddHours(1) });
        await context.SaveChangesAsync(Token);
        var sync = scope.ServiceProvider.GetRequiredService<CatalogueSync>();
        Assert.Equal(CatalogueSyncOutcome.Pending, (await sync.ReadAsync(Token))[0].Outcome);

        // A nonempty final page still retains its UUID; HasMore is the completion proof.
        await cursors.SavePageAsync(Feed.WantedVideos, FeedPosition.MidWalkAt(now, Guid.NewGuid()), false, Token);
        Assert.Equal(CatalogueSyncOutcome.Current, (await sync.ReadAsync(Token))[0].Outcome);
        db.Time.Advance(TimeSpan.FromMinutes(1));
        await cursors.SavePageAsync(Feed.WantedVideos, FeedPosition.MidWalkAt(now, Guid.NewGuid()), true, Token);
        var partial = (await sync.ReadAsync(Token))[0];
        Assert.Equal(CatalogueSyncOutcome.Pending, partial.Outcome);
        Assert.Equal(now, partial.LastSyncedAt);

        await cursors.SavePageAsync(Feed.WantedVideos, null, false, Token);
        Assert.Equal(CatalogueSyncOutcome.Pending, (await sync.ReadAsync(Token))[0].Outcome);
        await cursors.SavePageAsync(Feed.WantedVideos, FeedPosition.CaughtUpAt(now), false, Token);
        Assert.Equal(db.Time.GetUtcNow(), (await sync.ReadAsync(Token))[0].LastSyncedAt);

        await context.Routines.ExecuteUpdateAsync(u => u.SetProperty(row => row.RunNowPending, true), Token);
        Assert.Equal(CatalogueSyncOutcome.Scheduled, (await sync.ReadAsync(Token))[0].Outcome);
        await context.Routines.ExecuteUpdateAsync(u => u.SetProperty(row => row.ConsecutiveFailures, 1), Token);
        Assert.Equal(CatalogueSyncOutcome.Failed, (await sync.ReadAsync(Token))[0].Outcome);
        await context.Routines.ExecuteUpdateAsync(u => u.SetProperty(row => row.DeferredUntil, now.AddHours(1)), Token);
        Assert.Equal(CatalogueSyncOutcome.Deferred, (await sync.ReadAsync(Token))[0].Outcome);
    }

    [Fact]
    public async Task Actors_are_current_only_when_both_metadata_and_favourites_are_complete_and_not_overdue()
    {
        await using var db = await TestDatabase.CreateAsync(also: services => services.AddFabSync());
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var cursors = scope.ServiceProvider.GetRequiredService<FeedCursors>();
        var now = db.Time.GetUtcNow();
        context.Routines.AddRange(
            new() { Name = ActorFeedRoutine.RoutineName, DueAt = now.AddHours(6) },
            new() { Name = FavouriteActorFeedRoutine.RoutineName, DueAt = now.AddHours(1) });
        await context.SaveChangesAsync(Token);
        var sync = scope.ServiceProvider.GetRequiredService<CatalogueSync>();
        await cursors.SavePageAsync(Feed.Actors, FeedPosition.CaughtUpAt(now), false, Token);
        Assert.Equal(CatalogueSyncOutcome.Pending, (await sync.ReadAsync(Token))[1].Outcome);
        db.Time.Advance(TimeSpan.FromMinutes(2));
        await cursors.SavePageAsync(Feed.FavouriteActors, FeedPosition.CaughtUpAt(now), false, Token);
        var complete = (await sync.ReadAsync(Token))[1];
        Assert.Equal(CatalogueSyncOutcome.Current, complete.Outcome);
        Assert.Equal(now, complete.LastSyncedAt);
        db.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(CatalogueSyncOutcome.Pending, (await sync.ReadAsync(Token))[1].Outcome);
    }
}
