using System.Globalization;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Fab.Infrastructure.Tests.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Connections;

/// <summary>
/// A key belonging to a different prdb account: what goes, what stays, and what
/// the schema is asked rather than told.
/// </summary>
public sealed class AnotherAccountTests
{
    private const string Identity = "/user-identity";
    private const string Wanted = "/wanted-videos/changes";

    private const string TheirKey = "0123456789abcdef0123456789abcdef";
    private const string SomebodyElsesKey = "fedcba9876543210fedcba9876543210";

    private const string TheirHash = "5b1f0c2e9a7d4f3b8c6e1a0d2f4b6a8c";
    private const string SomebodyElsesHash = "9d3a7c1e5f8b2046ae7c9b1d3f5a7c90";

    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid AVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid ASite = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid AnActor = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>
    /// ADR 0013's sentence, as a test: the user half goes and the catalogue
    /// stays, because the catalogue belongs to no account. The cursors go with
    /// the list they walked — one from another account's feed would silently
    /// start the new account's list in the middle.
    /// </summary>
    [Fact]
    public async Task Changing_to_a_key_from_another_account_takes_the_user_half_and_leaves_the_catalogue()
    {
        var prdb = new FakePrdbApi()
            .Answers(Identity, IdentityOf(TheirHash))
            .Answers(Identity, IdentityOf(SomebodyElsesHash));

        await using var database = await CreateAsync(prdb);

        Assert.Equal(PrdbConnectionOutcome.Saved, await SaveAsync(database, TheirKey));

        var held = await FillAsync(database);

        Assert.Equal(
            PrdbConnectionOutcome.Saved,
            await SaveAsync(database, SomebodyElsesKey, confirmed: true));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.WantedVideos.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.FavouriteSites.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.FavouriteActors.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.WantedVideoSweepStates.CountAsync(TestContext.Current.CancellationToken));

        // The three that walked the user's own feeds, and only those three.
        var cursors = await context.FeedCursors
            .Select(row => row.Feed)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Feed.WantedVideos, cursors);
        Assert.DoesNotContain(Feed.FavouriteSites, cursors);
        Assert.DoesNotContain(Feed.FavouriteActors, cursors);
        Assert.Contains(Feed.WhatsNew, cursors);
        Assert.Contains(Feed.Sites, cursors);

        // The catalogue belongs to no account, and neither does the row that
        // holds the key.
        Assert.Equal(held, await context.CatalogueVideos.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CatalogueSites.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CatalogueActors.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Releases.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.ReportedStates.CountAsync(TestContext.Current.CancellationToken));

        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SomebodyElsesKey, installation.PrdbApiKey);
        Assert.Equal(SomebodyElsesHash, installation.PrdbUserHash);
    }

    /// <summary>
    /// ADR 0010 confirms rather than blocks, and nothing happens until it has
    /// been confirmed — the point of the confirmation is that the consequence is
    /// named before it happens rather than discovered afterwards as a wanted
    /// list that emptied itself.
    /// </summary>
    [Fact]
    public async Task Nothing_is_dropped_until_the_change_has_been_confirmed()
    {
        var prdb = new FakePrdbApi()
            .Answers(Identity, IdentityOf(TheirHash))
            .Answers(Identity, IdentityOf(SomebodyElsesHash));

        await using var database = await CreateAsync(prdb);

        await SaveAsync(database, TheirKey);
        await FillAsync(database);

        Assert.Equal(
            PrdbConnectionOutcome.AnotherAccount,
            await SaveAsync(database, SomebodyElsesKey));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(1, await context.WantedVideos.CountAsync(TestContext.Current.CancellationToken));

        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TheirKey, installation.PrdbApiKey);
        Assert.Equal(TheirHash, installation.PrdbUserHash);
    }

    /// <summary>
    /// The same key twice is not a change of account, so nothing is dropped —
    /// which matters because ADR 0020 has a person re-save this form to correct
    /// anything else on it.
    /// </summary>
    [Fact]
    public async Task Saving_the_same_account_again_drops_nothing()
    {
        var prdb = new FakePrdbApi().Answers(Identity, IdentityOf(TheirHash));

        await using var database = await CreateAsync(prdb);

        await SaveAsync(database, TheirKey);
        await FillAsync(database);

        Assert.Equal(PrdbConnectionOutcome.Saved, await SaveAsync(database, TheirKey));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(1, await context.WantedVideos.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.FavouriteSites.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The cursors are reset rather than resumed, so the next run of a feed
    /// reads the new account's list from the beginning — which is what an absent
    /// <c>since</c> means — and nothing of the old one survives in it.
    /// </summary>
    [Fact]
    public async Task The_next_feed_run_fills_the_list_from_the_new_account()
    {
        var theirs = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000a");
        var somebody = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000b");

        var prdb = new FakePrdbApi()
            .Answers(Identity, IdentityOf(TheirHash))
            .Answers(Identity, IdentityOf(SomebodyElsesHash))
            .Answers(Wanted, WantedPage(theirs, "Theirs"))
            .Answers(Wanted, WantedPage(somebody, "Somebody Else's"));

        await using var database = await CreateAsync(prdb);

        await SaveAsync(database, TheirKey);
        await RunWantedAsync(database);

        Assert.Equal([theirs], await WantedAsync(database));

        await SaveAsync(database, SomebodyElsesKey, confirmed: true);

        Assert.Empty(await WantedAsync(database));

        await RunWantedAsync(database);

        Assert.Equal([somebody], await WantedAsync(database));

        // And the run that filled it asked from the beginning of time rather
        // than from where the other account's walk had come to. The cursor
        // going with the key is what this is about; the bound is there because
        // prdb refuses a feed request without one.
        var asked = prdb.AskedFor(Wanted)[^1];

        Assert.Equal(
            DateTimeOffset.MinValue,
            DateTimeOffset.Parse(
                HttpUtility.ParseQueryString(asked.Query)["Since"]!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    /// <summary>
    /// The list of deletes is read off the schema (ADR 0033), so it is exactly
    /// the tables that declare themselves account-scoped — not a list somebody
    /// has to keep in step. The guard that makes this worth anything is
    /// <c>CatalogueSchemaTests</c>, which fails over a table that declares
    /// nothing at all.
    /// </summary>
    [Fact]
    public async Task The_tables_that_go_are_the_ones_the_schema_says_go()
    {
        await using var database = await CreateAsync(new FakePrdbApi());
        await using var scope = database.Scope();

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var declared = context.Model.GetEntityTypes()
            .Where(entity => AccountClasses.DeclaredBy(entity) == AccountClass.AccountScoped)
            .Select(entity => entity.GetTableName())
            .OrderBy(table => table, StringComparer.Ordinal)
            .ToList();

        var dropped = scope.ServiceProvider.GetRequiredService<AccountScopedRows>().Tables;

        Assert.Equal(declared, [.. dropped.OrderBy(table => table, StringComparer.Ordinal)]);

        // The three ADR 0013 choices and the per-account position of each
        // wanted-video/indexer sweep go. The release cache itself belongs to no
        // account and deliberately is not here.
        Assert.Equal(
            ["favourite_actor", "favourite_site", "wanted_video", "wanted_video_sweep_state"],
            declared);

        // ADR 0019's record of what was reported is account-stamped rather than
        // account-scoped, so this list is not where it lands.
        Assert.DoesNotContain(
            context.Model.GetEntityTypes()
                .Where(entity => AccountClasses.DeclaredBy(entity) == AccountClass.AccountStamped)
                .Select(entity => entity.GetTableName()),
            dropped.Contains);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb) =>
        await TestDatabase.CreateAsync(prdb: prdb, also: services => services.AddFabSync());

    private static async Task<PrdbConnectionOutcome> SaveAsync(
        TestDatabase database,
        string apiKey,
        bool confirmed = false)
    {
        await using var scope = database.Scope();

        var save = await scope.ServiceProvider.GetRequiredService<PrdbConnections>()
            .SaveAsync(apiKey, confirmed, TestContext.Current.CancellationToken);

        return save.Outcome;
    }

    /// <summary>
    /// One account's answers, as far as this slice has them: a wanted video, a
    /// favourite site, a favourite actor, the three cursors that walked them,
    /// and two cursors that belong to nobody.
    /// </summary>
    /// <returns>How many catalogue videos there are, which is what must not change.</returns>
    private static async Task<int> FillAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = new CatalogueVideoRow { PrdbId = AVideo, Title = "A Video", NormalisedTitle = "a video" };
        var site = new CatalogueSiteRow { PrdbId = ASite, Title = "A Site" };
        var actor = new CatalogueActorRow { PrdbId = AnActor, Name = "Jane Doe" };

        context.CatalogueVideos.Add(video);
        context.CatalogueSites.Add(site);
        context.CatalogueActors.Add(actor);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.WantedVideos.Add(new WantedVideoRow { VideoId = video.Id, SinceAt = Noon });
        context.FavouriteSites.Add(new FavouriteSiteRow { SiteId = site.Id, SinceAt = Noon });
        context.FavouriteActors.Add(new FavouriteActorRow { ActorId = actor.Id, SinceAt = Noon });

        var indexer = new IndexerRow
        {
            Id = Guid.Parse("0198ec28-1c00-7000-8000-000000000003"),
            Name = "An indexer",
            Url = "https://indexer.invalid/api",
            ApiKey = "indexer-key",
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = Noon,
        };
        context.Indexers.Add(indexer);
        context.Releases.Add(new ReleaseRow
        {
            Indexer = indexer,
            DerivedReleaseId = "release-id",
            Title = "A release",
            NormalisedTitle = "a release",
            PostDate = Noon,
            PubDate = Noon,
            FirstSeenAt = Noon,
        });
        context.WantedVideoSweepStates.Add(new WantedVideoSweepStateRow
        {
            Video = context.WantedVideos.Local.Single(),
            Indexer = indexer,
            LastSearchedAt = Noon,
        });
        context.ReportedStates.Add(new ReportedStateRow
        {
            VideoId = AVideo,
            UserHash = TheirHash,
            IsFulfilled = true,
            Quality = FulfilmentQuality.P1080,
            FulfilledAt = Noon,
        });

        foreach (var feed in Feeds.All)
        {
            context.FeedCursors.Add(new FeedCursorRow
            {
                Feed = feed,
                Cursor = FeedPosition.CaughtUpAt(Noon).Stored,
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return 1;
    }

    private static async Task RunWantedAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<WantedVideoFeedRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<Guid>> WantedAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .WantedVideos
            .Select(row => row.Video!.PrdbId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static string IdentityOf(string userHash) =>
        $$"""{"userHash":"{{userHash}}","activeSubscriptions":[]}""";

    private static string WantedPage(Guid videoId, string title) =>
        $$"""
        {
          "items": [
            {
              "eventType": "created",
              "wantedVideo": {
                "videoId": "{{videoId}}",
                "videoTitle": "{{title}}",
                "siteTitle": "A Site",
                "isDeleted": false,
                "isFulfilled": false,
                "createdAtUtc": "{{Stamp(Noon)}}",
                "updatedAtUtc": "{{Stamp(Noon)}}"
              }
            }
          ],
          "pageSize": 1000,
          "hasMore": false,
          "serverTimeUtc": "{{Stamp(Noon)}}",
          "nextCursor": { "updatedAtUtc": "{{Stamp(Noon)}}", "id": "{{videoId}}" }
        }
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
