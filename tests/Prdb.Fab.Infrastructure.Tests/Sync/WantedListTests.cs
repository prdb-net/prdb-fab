using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// The wanted list as a surface: what ticket 03's feed writes, read back the
/// way the grid reads it, and the two facts about the sync that decide what an
/// empty one says.
/// </summary>
public sealed class WantedListTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Wanted = "/wanted-videos/changes";

    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static Guid Video(int number) => Guid.Parse($"aaaaaaaa-0000-4000-8000-{number:D12}");

    /// <summary>
    /// The path end to end: marked in prdb, read by the feed on its own cadence,
    /// and on the surface — with the catalogue row the payload filled, which is
    /// what carries the title, the site and the artwork the card asks for.
    /// </summary>
    [Fact]
    public async Task A_video_wanted_in_prdb_reaches_the_list_without_a_restart()
    {
        var prdb = new FakePrdbApi().Answers(Wanted, Page(Video(1), "A Video", Noon));

        await using var database = await CreateAsync(prdb);

        // Before the feed has run there is nothing, and the surface says which
        // kind of nothing it is.
        var before = await ReadAsync(database);

        Assert.Empty(before.Videos);
        Assert.False(before.FeedHasRun);

        await RunFeedAsync(database);

        var after = await ReadAsync(database);

        var card = Assert.Single(after.Videos);

        Assert.Equal("A Video", card.Title);
        Assert.Equal(Video(1), card.PrdbId);
        Assert.True(after.FeedHasRun);
    }

    /// <summary>
    /// Two empty lists that look identical and are not: an account with nothing
    /// marked, and a list that has not been read yet. Telling somebody there is
    /// nothing on their list when it simply has not arrived is the difference
    /// between a tool that is working and one that looks broken.
    /// </summary>
    [Fact]
    public async Task An_empty_list_and_a_list_that_has_not_been_read_are_different_states()
    {
        var prdb = new FakePrdbApi().Answers(Wanted, Nothing(Noon));

        await using var database = await CreateAsync(prdb);

        Assert.False((await ReadAsync(database)).FeedHasRun);

        await RunFeedAsync(database);

        var read = await ReadAsync(database);

        Assert.Empty(read.Videos);
        Assert.True(read.FeedHasRun);
    }

    /// <summary>
    /// Newest wanting first, which is prdb's own stamp rather than when a feed
    /// read it — so a key put on a second installation builds the list in the
    /// order the user built it.
    /// </summary>
    [Fact]
    public async Task The_list_is_newest_wanting_first()
    {
        await using var database = await CreateAsync(new FakePrdbApi());

        await WantAsync(database, Video(1), "Wanted a week ago", Noon.AddDays(-7));
        await WantAsync(database, Video(2), "Wanted this morning", Noon.AddHours(-4));
        await WantAsync(database, Video(3), "Wanted a month ago", Noon.AddDays(-30));

        var read = await ReadAsync(database);

        Assert.Equal(
            ["Wanted this morning", "Wanted a week ago", "Wanted a month ago"],
            read.Videos.Select(video => video.Title));
    }

    [Fact]
    public async Task Becoming_wanted_puts_an_already_held_title_back_into_backwards_screening()
    {
        var prdb = new FakePrdbApi().Answers(Wanted, Page(Video(1), "A Video", Noon));
        await using var database = await CreateAsync(prdb);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.CatalogueVideos.Add(new CatalogueVideoRow
            {
                PrdbId = Video(1),
                Title = "A Video",
                NormalisedTitle = ComparisonForm.Of("A Video"),
                TitleSearchedBackwards = true,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunFeedAsync(database);

        await using var check = database.Scope();
        Assert.False((await check.ServiceProvider.GetRequiredService<FabDbContext>()
            .CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken)).TitleSearchedBackwards);
    }

    /// <summary>
    /// ADR 0013: a running backfill is a fact and explicitly not a Gap. The row
    /// being there is the whole of what <em>still running</em> means (ADR 0014:
    /// bootstrap is not a state of the application), so the line goes when the
    /// routine retires and nothing else has to be told.
    /// </summary>
    [Fact]
    public async Task The_backfill_line_appears_while_it_has_a_row_and_goes_when_it_retires()
    {
        await using var database = await CreateAsync(new FakePrdbApi());

        await database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);

        Assert.True((await ReadAsync(database)).BackfillRunning);

        // Retiring is deleting the row, which is what the routine does when it
        // reaches its page ceiling.
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Routines
                .Where(row => row.Name == WhatsNewBackfillRoutine.RoutineName)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        Assert.False((await ReadAsync(database)).BackfillRunning);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddFabSync());

        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation
            .ExecuteUpdateAsync(
                row => row.SetProperty(installation => installation.PrdbApiKey, ApiKey),
                TestContext.Current.CancellationToken);

        return database;
    }

    private static async Task<WantedList> ReadAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<CatalogueBrowse>()
            .WantedAsync(page: 1, TestContext.Current.CancellationToken);
    }

    private static async Task RunFeedAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<WantedVideoFeedRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A wanted row written straight, for the tests that are about the order the
    /// surface reads in rather than about how the row got there.
    /// </summary>
    private static async Task WantAsync(
        TestDatabase database,
        Guid prdbId,
        string title,
        DateTimeOffset since)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = new CatalogueVideoRow { PrdbId = prdbId, Title = title };

        context.CatalogueVideos.Add(video);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.WantedVideos.Add(new WantedVideoRow { VideoId = video.Id, SinceAt = since });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static string Page(Guid videoId, string title, DateTimeOffset at) =>
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
                "createdAtUtc": "{{Stamp(at)}}",
                "updatedAtUtc": "{{Stamp(at)}}"
              }
            }
          ],
          "pageSize": 1000,
          "hasMore": false,
          "serverTimeUtc": "{{Stamp(at)}}",
          "nextCursor": { "updatedAtUtc": "{{Stamp(at)}}", "id": "{{videoId}}" }
        }
        """;

    private static string Nothing(DateTimeOffset at) =>
        $$"""
        {
          "items": [],
          "pageSize": 1000,
          "hasMore": false,
          "serverTimeUtc": "{{Stamp(at)}}"
        }
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
