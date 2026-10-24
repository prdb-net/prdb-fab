using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class SiteChangeTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly DateTimeOffset Stamp = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_multipage_baseline_resumes_and_retires_the_legacy_list_only_at_the_end()
    {
        var api = new FakePrdbApi().Answers("/sites/changes", Page("First", Stamp, more: true))
            .Answers("/sites/changes", Page("Corrected", Stamp.AddMinutes(1)));
        await using var db = await CreateAsync(api);
        await db.Services.PrepareFabScheduleAsync(Token);
        await RunAsync(db);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.False(await context.FeedCursors.AnyAsync(c => c.Feed == Feed.SiteChangesBaseline, Token));
            var progress = await context.FeedCursors.SingleAsync(c => c.Feed == Feed.SiteChanges, Token);
            Assert.True(progress.HasMore);
            Assert.Null(progress.LastSyncedAt);
            Assert.True(await context.Routines.AnyAsync(r => r.Name == SiteListRoutine.RoutineName, Token));
        }
        await RunAsync(db);
        await using var after = db.Scope();
        var rows = after.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.True(await rows.FeedCursors.AnyAsync(c => c.Feed == Feed.SiteChangesBaseline, Token));
        var complete = await rows.FeedCursors.SingleAsync(c => c.Feed == Feed.SiteChanges, Token);
        Assert.False(complete.HasMore);
        Assert.Equal(db.Time.GetUtcNow(), complete.LastSyncedAt);
        Assert.False(await rows.Routines.AnyAsync(r => r.Name == SiteListRoutine.RoutineName, Token));
        Assert.Equal("Corrected", (await rows.CatalogueSites.SingleAsync(Token)).Title);
        Assert.Contains(Site.ToString(), api.AskedFor("/sites/changes")[1].Query);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task Tombstones_retain_the_last_metadata_and_favourite_identity_then_restore_in_place()
    {
        var api = new FakePrdbApi().Answers("/sites/changes", Page("Creator", Stamp))
            .Answers("/sites/changes", Page(null, Stamp.AddHours(1), deleted: true))
            .Answers("/sites/changes", Page("Stale", Stamp))
            .Answers("/sites/changes", Page("Restored", Stamp.AddHours(2)));
        await using var db = await CreateAsync(api);
        await RunAsync(db);
        long local;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var site = await context.CatalogueSites.SingleAsync(Token);
            local = site.Id;
            Assert.Equal("Network", site.Network);
            Assert.Equal("Platform", site.PlatformTitle);
            var metadata = SiteMetadata.Of(site);
            Assert.Equal("https://example.test/creator", metadata.Url);
            Assert.Equal("Home", Assert.Single(metadata.Links).Label);
            context.FavouriteSites.Add(new FavouriteSiteRow { SiteId = local });
            await context.SaveChangesAsync(Token);
        }
        await RunAsync(db);
        await RunAsync(db);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var site = await context.CatalogueSites.SingleAsync(Token);
            Assert.False(site.StillOffered);
            Assert.Equal("Creator", site.Title);
            Assert.Equal(local, (await context.FavouriteSites.SingleAsync(Token)).SiteId);
        }
        await RunAsync(db);
        await using var after = db.Scope();
        var restored = await after.ServiceProvider.GetRequiredService<FabDbContext>().CatalogueSites.SingleAsync(Token);
        Assert.True(restored.StillOffered);
        Assert.Equal(local, restored.Id);
        Assert.Equal("Restored", restored.Title);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi api)
    {
        var db = await TestDatabase.CreateAsync(prdb: api, also: s => s.AddFabSync());
        await using var scope = db.Scope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(u => u.SetProperty(i => i.PrdbApiKey, "fixture"), Token);
        return db;
    }
    private static async Task RunAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<SiteFeedRoutine>().RunAsync(null, Token);
        if (result.DueIn is not null) Assert.Equal(TimeSpan.FromSeconds(10), result.DueIn);
    }
    private static string Page(string? title, DateTimeOffset at, bool more = false, bool deleted = false) => $$$"""
        {"items":[{"eventType":"{{{(deleted ? "deleted" : "updated")}}}","site":{"id":"{{{Site}}}","title":{{{System.Text.Json.JsonSerializer.Serialize(title)}}},"url":"https://example.test/creator","links":[{"label":"Home","url":"https://example.test/home"},{"label":"Bad","url":"javascript:alert(1)"}],"networkTitle":"Network","platformTitle":"Platform","accountHandle":"creator","isDeleted":{{{(deleted ? "true" : "false")}}},"updatedAtUtc":"{{{at:O}}}"}}],"hasMore":{{{(more ? "true" : "false")}}},"serverTimeUtc":"{{{at:O}}}","nextCursor":{"id":"{{{Site}}}","updatedAtUtc":"{{{at:O}}}"}}
        """;
}
