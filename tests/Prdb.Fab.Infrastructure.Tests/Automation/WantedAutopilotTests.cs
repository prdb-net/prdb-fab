using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Automation;

public sealed class WantedAutopilotTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initial_membership_is_newest_100_and_never_refills_or_admits_corrected_old_backlog()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db, 110);
        await ReconcileAsync(db);
        Guid[] expected;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            expected = (await context.CatalogueVideos.ToListAsync(Token)).OrderByDescending(v => v.ReleaseDate).ThenBy(v => v.PrdbId).Take(100).Select(v => v.PrdbId).ToArray();
            Assert.Equal(expected.Order(), (await context.AutopilotMemberships.Where(m => m.Admitted).Select(m => m.VideoId).ToListAsync(Token)).Order());
            var first = expected[0];
            await context.WantedVideos.Where(w => w.Video!.PrdbId == first).ExecuteDeleteAsync(Token);
            context.LibraryEntries.Add(new() { VideoId = expected[1], EntryDirectory = "/held" });
            await context.CatalogueVideos.Where(v => !expected.Contains(v.PrdbId))
                .ExecuteUpdateAsync(u => u.SetProperty(v => v.ReleaseDate, DateOnly.FromDateTime(db.Time.GetUtcNow().DateTime)), Token);
            await context.SaveChangesAsync(Token);
            await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().SetEnabledAsync(false, Token);
            await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().SetEnabledAsync(true, Token);
        }
        await ReconcileAsync(db);
        await using var read = db.Scope();
        var after = read.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(expected.Order(), (await after.AutopilotMemberships.Where(m => m.Admitted).Select(m => m.VideoId).ToListAsync(Token)).Order());
        Assert.Equal(110, await after.AutopilotMemberships.CountAsync(Token));
    }

    [Fact]
    public async Task Baseline_waits_for_complete_inventory_feed_and_metadata_and_preserves_date_edges()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db, 6);
        var today = DateOnly.FromDateTime(db.Time.GetUtcNow().DateTime);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var rows = await context.CatalogueVideos.AsTracking().OrderBy(v => v.Id).ToListAsync(Token);
            DateOnly?[] dates = [today, today.AddDays(-89), today.AddDays(-90), today.AddDays(1), null, today];
            for (var i = 0; i < rows.Count; i++) rows[i].ReleaseDate = dates[i];
            rows[5].LastReadAt = default;
            await context.SaveChangesAsync(Token);
        }
        Assert.Equal(0, await ReconcileAsync(db));
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.CatalogueVideos.ExecuteUpdateAsync(u => u.SetProperty(v => v.LastReadAt, db.Time.GetUtcNow()), Token);
            await context.CollectionPreparations.ExecuteUpdateAsync(u => u.SetProperty(p => p.Phase, "Preview"), Token);
        }
        Assert.Equal(0, await ReconcileAsync(db));
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.CollectionPreparations.ExecuteUpdateAsync(u => u.SetProperty(p => p.Phase, "Ready"), Token);
            await context.FeedCursors.ExecuteUpdateAsync(u => u.SetProperty(p => p.Cursor, FeedPosition.MidWalkAt(db.Time.GetUtcNow(), Guid.NewGuid()).Stored), Token);
        }
        Assert.Equal(0, await ReconcileAsync(db));
        await using (var scope = db.Scope())
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().FeedCursors.ExecuteUpdateAsync(u => u.SetProperty(p => p.Cursor, FeedPosition.CaughtUpAt(db.Time.GetUtcNow()).Stored), Token);
        Assert.True(await ReconcileAsync(db) > 0);
        await using var read = db.Scope();
        Assert.Equal(3, await read.ServiceProvider.GetRequiredService<FabDbContext>().AutopilotMemberships.CountAsync(m => m.Admitted, Token));
    }

    [Fact]
    public async Task New_intent_is_idempotent_and_all_enabled_indexers_share_the_supplied_permission()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db, 1);
        await ReconcileAsync(db);
        db.Time.Advance(TimeSpan.FromMinutes(5));
        Guid fresh;
        Guid indexer;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var video = new CatalogueVideoRow { PrdbId = Guid.NewGuid(), Title = "New intent", ReleaseDate = DateOnly.FromDateTime(db.Time.GetUtcNow().DateTime), LastReadAt = db.Time.GetUtcNow() };
            context.CatalogueVideos.Add(video);
            await context.SaveChangesAsync(Token);
            context.WantedVideos.Add(new() { VideoId = video.Id, SinceAt = db.Time.GetUtcNow() });
            fresh = video.PrdbId;
            indexer = Guid.NewGuid();
            context.Indexers.Add(new() { Id = indexer, Name = "Late Indexer", Url = "http://late.invalid", Enabled = true });
            await context.SaveChangesAsync(Token);
        }
        Assert.Equal(1, await ReconcileAsync(db));
        Assert.Equal(0, await ReconcileAsync(db));
        await using var read = db.Scope();
        var eligibility = read.ServiceProvider.GetRequiredService<AutomaticEligibility>();
        ReleaseChoice[] choices = [new(1, indexer, "Late Indexer", "release", "release", 100, IdentificationConfidence.Exact, 1, null)];
        Assert.Equal(WantedAutopilot.PolicyName, Assert.Single((await eligibility.ForVideoAsync(fresh, choices, Token))[1].Rules).Name);
        var contextAfter = read.ServiceProvider.GetRequiredService<FabDbContext>();
        await contextAfter.CatalogueVideos.Where(v => v.PrdbId == fresh).ExecuteUpdateAsync(u => u.SetProperty(v => v.ReleaseDate, new DateOnly(2000, 1, 1)), Token);
        Assert.Equal(AutomationDecisionReason.OutsideRecentWanted, (await eligibility.ForVideoAsync(fresh, choices, Token))[1].Reason);
        await read.ServiceProvider.GetRequiredService<WantedAutopilot>().SetEnabledAsync(false, Token);
        Assert.False((await eligibility.ForVideoAsync(fresh, choices, Token))[1].Eligible);
    }

    [Theory]
    [InlineData("Inventory", null, "Brake")]
    [InlineData("Preview", null, "Brake")]
    [InlineData("Importing", null, "Brake")]
    [InlineData("Inventory", "The mounted collection is unreadable.", "Gap")]
    public async Task Preparation_status_names_its_action_and_distinguishes_storage_failure(string phase, string? error, string kind)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db, 0);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.CollectionPreparations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Phase, phase).SetProperty(row => row.Error, error), Token);
        var status = await scope.ServiceProvider.GetRequiredService<Prdb.Fab.Infrastructure.Status.StatusService>().ReadAsync(Token);
        var condition = status.Stages.SelectMany(stage => stage.Gaps.Concat(stage.Brakes)).First(row => row.Title.StartsWith("Collection preparation", StringComparison.Ordinal));
        Assert.Equal(kind, condition.Kind.ToString());
        Assert.Equal("/settings/library/collection?from=/status", condition.Route);
    }

    [Fact]
    public async Task Changing_accounts_establishes_a_new_baseline_and_returning_preserves_the_original_selection()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db, 110);
        await ReconcileAsync(db);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await scope.ServiceProvider.GetRequiredService<AccountScopedRows>().DropAsync(Token);
            await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.PrdbUserHash, "second-account"), Token);
        }
        Assert.Equal(0, await ReconcileAsync(db));
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var videos = await context.CatalogueVideos.ToListAsync(Token);
            context.WantedVideos.AddRange(videos.Select(video => new WantedVideoRow { VideoId = video.Id, SinceAt = db.Time.GetUtcNow().AddDays(-10) }));
            context.FeedCursors.Add(new() { Feed = Feed.WantedVideos, Cursor = FeedPosition.CaughtUpAt(db.Time.GetUtcNow()).Stored });
            await context.SaveChangesAsync(Token);
        }
        await ReconcileAsync(db);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(2, await context.AutopilotBaselines.CountAsync(Token));
            Assert.Equal(100, await context.AutopilotMemberships.CountAsync(row => row.UserHash == "second-account" && row.Admitted, Token));
            await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().SetEnabledAsync(false, Token);
            await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.PrdbUserHash, "fixture-account"), Token);
            Assert.False((await context.AutomationRules.SingleAsync(row => row.SuppliedWanted, Token)).Enabled);
            await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().SetEnabledAsync(true, Token);
        }
        Assert.Equal(0, await ReconcileAsync(db));
        await using var read = db.Scope();
        Assert.Equal(100, await read.ServiceProvider.GetRequiredService<FabDbContext>().AutopilotMemberships.CountAsync(row => row.UserHash == "fixture-account" && row.Admitted, Token));
    }

    private static async Task<int> ReconcileAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().ReconcileAsync(Token);
    }
    internal static async Task SeedAsync(TestDatabase db, int count)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var now = db.Time.GetUtcNow();
        await context.Installation.ExecuteUpdateAsync(u => u.SetProperty(i => i.OnboardingStep, OnboardingStep.Complete)
            .SetProperty(i => i.PrdbUserHash, "fixture-account").SetProperty(i => i.PrdbApiKey, "fixture")
            .SetProperty(i => i.SabnzbdApiKey, "fixture"), Token);
        context.CollectionPreparations.Add(new() { Phase = "Ready", StartedAt = now });
        context.Indexers.Add(new() { Id = Guid.NewGuid(), Name = "Indexer", Enabled = true });
        context.FeedCursors.Add(new() { Feed = Feed.WantedVideos, Cursor = FeedPosition.CaughtUpAt(now).Stored });
        for (var i = 0; i < count; i++)
        {
            var video = new CatalogueVideoRow { PrdbId = Guid.NewGuid(), Title = "Video " + i,
                ReleaseDate = DateOnly.FromDateTime(now.DateTime).AddDays(-(i / 2)), LastReadAt = now };
            context.CatalogueVideos.Add(video);
            await context.SaveChangesAsync(Token);
            context.WantedVideos.Add(new() { VideoId = video.Id, SinceAt = now.AddDays(-100) });
        }
        await context.SaveChangesAsync(Token);
        await scope.ServiceProvider.GetRequiredService<WantedAutopilot>().SetEnabledAsync(true, Token);
    }
}
