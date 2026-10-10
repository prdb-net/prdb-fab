using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

public sealed class IgnoredSiteTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Video = Guid.NewGuid();
    private static readonly DateTimeOffset Stamp = new(2026, 8, 27, 10, 0, 0, TimeSpan.Zero);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ignore_blocks_pending_intent_abandons_only_automatic_downloads_and_replay_is_safe()
    {
        var api = new FakePrdbApi().Answers("/ignored-sites/changes", Page(false, Stamp));
        await using var db = await CreateAsync(api);
        await using (var scope = db.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountPreferences>().StageWantedAsync(Video, Stamp.AddMinutes(-1), Token);
        }
        await RunAsync(db);
        await RunAsync(db);
        await using var after = db.Scope();
        var context = after.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.False(await context.WantedVideos.AnyAsync(Token));
        Assert.False(await context.FavouriteSites.AnyAsync(Token));
        var write = await context.AccountPreferenceWrites.SingleAsync(Token);
        Assert.True(write.Blocked);
        Assert.Contains("ignored", write.LastFailure);
        var downloads = await context.Downloads.ToListAsync(Token);
        Assert.Equal(DownloadState.Abandoned, downloads.Single(d => !d.OriginIsPerson).State);
        Assert.Equal(DownloadState.Outstanding, downloads.Single(d => d.OriginIsPerson).State);
        var ignores = after.ServiceProvider.GetRequiredService<IgnoredSites>();
        Assert.True(await ignores.RestrictsAsync(AccountPreferenceKind.WantedVideo, Video, Token));
        Assert.Equal(AccountPreferenceOutcome.Rejected, (await after.ServiceProvider.GetRequiredService<AccountPreferences>()
            .SetAsync(AccountPreferenceKind.FavouriteSite, Site, true, Token)).Outcome);
        Assert.Equal(2, api.Requests.Count);
    }

    [Fact]
    public async Task Unignore_never_restores_old_intent_and_older_delivery_cannot_reverse_it()
    {
        var api = new FakePrdbApi().Answers("/ignored-sites/changes", Page(false, Stamp))
            .Answers("/ignored-sites/changes", Page(true, Stamp.AddHours(1)))
            .Answers("/ignored-sites/changes", Page(false, Stamp));
        await using var db = await CreateAsync(api);
        await RunAsync(db);
        await RunAsync(db);
        await RunAsync(db);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.False((await context.IgnoredSites.SingleAsync(Token)).Ignored);
        Assert.False(await context.WantedVideos.AnyAsync(Token));
        Assert.False(await context.FavouriteSites.AnyAsync(Token));
        var ignores = scope.ServiceProvider.GetRequiredService<IgnoredSites>();
        Assert.True(await ignores.RejectsWantedFeedAsync(Video, Stamp, Token));
        Assert.False(await ignores.RejectsWantedFeedAsync(Video, Stamp.AddHours(2), Token));
    }

    [Fact]
    public async Task Known_ignore_blocks_new_manual_Wanted_intent_without_a_remote_write_and_an_account_change_drops_it()
    {
        await using var db = await CreateAsync(new FakePrdbApi().Answers("/ignored-sites/changes", Page(false, Stamp)));
        await RunAsync(db);
        await using var scope = db.Scope();
        var preferences = scope.ServiceProvider.GetRequiredService<AccountPreferences>();
        await preferences.StageWantedAsync(Video, Stamp.AddHours(1), Token);
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.False(await context.WantedVideos.AnyAsync(Token));
        Assert.True((await context.AccountPreferenceWrites.SingleAsync(Token)).Blocked);
        await scope.ServiceProvider.GetRequiredService<AccountScopedRows>().DropAsync(Token);
        Assert.False(await context.IgnoredSites.AnyAsync(Token));
        Assert.False(await context.FeedCursors.AnyAsync(c => c.Feed == Feed.IgnoredSites, Token));
    }

    [Fact]
    public async Task Stale_favourite_reads_do_not_resurrect_an_ignored_site_and_outage_keeps_the_restriction()
    {
        var favourite = $$$"""{"items":[{"favoriteSite":{"id":"{{{Site}}}","title":"Site","updatedAtUtc":"{{{Stamp:O}}}"}}],"hasMore":false,"nextCursor":{"id":"{{{Site}}}","updatedAtUtc":"{{{Stamp:O}}}"}}""";
        var api = new FakePrdbApi().Answers("/ignored-sites/changes", Page(false, Stamp))
            .Refuses("/ignored-sites/changes", System.Net.HttpStatusCode.ServiceUnavailable, "Unavailable")
            .Answers("/favorite-sites/changes", favourite);
        await using var db = await CreateAsync(api);
        await RunAsync(db);
        await using (var scope = db.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FavouriteSiteFeedRoutine>().RunAsync(null, Token);
        }
        await Assert.ThrowsAnyAsync<Microsoft.Kiota.Abstractions.ApiException>(() => RunAsync(db));
        await using var after = db.Scope();
        var context = after.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.False(await context.FavouriteSites.AnyAsync(Token));
        Assert.True((await context.IgnoredSites.SingleAsync(Token)).Ignored);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi api)
    {
        var db = await TestDatabase.CreateAsync(prdb: api, also: s => s.AddFabSync());
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(u => u.SetProperty(i => i.PrdbApiKey, "fixture"), Token);
        var site = new CatalogueSiteRow { PrdbId = Site, Title = "Site" };
        var video = new CatalogueVideoRow { PrdbId = Video, Title = "Video", Site = site };
        context.AddRange(site, video);
        await context.SaveChangesAsync(Token);
        context.FavouriteSites.Add(new FavouriteSiteRow { SiteId = site.Id, SinceAt = Stamp.AddMinutes(-1) });
        var indexer = new IndexerRow { Id = Guid.NewGuid(), Name = "Fixture", Url = "https://indexer.invalid", ApiKey = "fixture", Categories = "Adult" };
        context.Indexers.Add(indexer);
        context.Downloads.AddRange(new[] { false, true }.Select(person => new DownloadRow
        {
            Id = Guid.NewGuid(), VideoId = Video, IndexerId = indexer.Id, DerivedReleaseId = person.ToString(),
            SubmittedName = "Fixture", State = DownloadState.Outstanding, OriginIsPerson = person, CreatedAt = Stamp,
        }));
        await context.SaveChangesAsync(Token);
        return db;
    }
    private static async Task RunAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        await scope.ServiceProvider.GetRequiredService<IgnoredSiteFeedRoutine>().RunAsync(null, Token);
    }
    private static string Page(bool deleted, DateTimeOffset at) => $$$"""
        {"items":[{"eventType":"updated","ignoredSite":{"id":"{{{Site}}}","isDeleted":{{{(deleted ? "true" : "false")}}},"updatedAtUtc":"{{{at:O}}}"}}],"hasMore":false,"serverTimeUtc":"{{{at:O}}}","nextCursor":{"id":"{{{Site}}}","updatedAtUtc":"{{{at:O}}}"}}
        """;
}
