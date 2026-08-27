using System.Globalization;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// The one prdb entity with no change feed: forwards from a high-water mark
/// every quarter of an hour, and backwards by the page until a ceiling.
/// </summary>
public sealed class WhatsNewTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Videos = "/videos";
    private const string Batch = "/videos/batch";

    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid First = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid ASite = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid AnActor = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    /// <summary>
    /// An image id is unique across the whole table, so a fixture that gave two
    /// videos the same one would be testing the schema rather than the routine.
    /// </summary>
    private static Guid ImageOf(Guid video) => Guid.Parse("dddddddd" + video.ToString("D")[8..]);

    /// <summary>
    /// ADR 0013's overlap over <c>CreatedAfter</c>, which the API documents as
    /// strictly exclusive: a mark at exactly the last value seen would lose
    /// every video created in the same instant, permanently, and a bulk import
    /// is exactly when several share one.
    /// </summary>
    [Fact]
    public async Task A_video_created_at_the_high_water_mark_is_still_picked_up()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, VideoPage([(First, Noon)]))
            .Answers(Batch, Details([(First, "The First")]))
            .Answers(Videos, VideoPage([(Second, Noon)]))
            .Answers(Batch, Details([(Second, "The Second")]));

        await using var database = await CreateAsync(prdb);

        await RunAsync<WhatsNewRoutine>(database);
        await RunAsync<WhatsNewRoutine>(database);

        var asked = prdb.AskedFor(Videos);

        // The first run had no mark, so it took the newest hundred, which is
        // what What's New means. The second walks forwards from the mark, and
        // asks from a minute before it.
        Assert.Equal("desc", Query(asked[0], "SortDirection"));
        Assert.Null(Query(asked[0], "CreatedAfter"));

        Assert.Equal("asc", Query(asked[1], "SortDirection"));
        Assert.Equal(Noon - FeedPosition.Overlap, Time(Query(asked[1], "CreatedAfter")));

        // And the video sharing the boundary timestamp is in the catalogue.
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(2, await context.CatalogueVideos.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0013 rests a whole argument on this: <c>VideoSummaryDto</c> carries
    /// no image field, so a row born from a summary would have no artwork and no
    /// way to know it was missing. Every row therefore comes from a detail read,
    /// and a page of a hundred costs one request to discover and two to read
    /// back at fifty a request.
    /// </summary>
    [Fact]
    public async Task A_catalogue_row_is_written_from_the_detail_read_and_not_from_the_summary()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, VideoPage([(First, Noon)]))
            .Answers(Batch, Details([(First, "The First")]));

        await using var database = await CreateAsync(prdb);

        await RunAsync<WhatsNewRoutine>(database);

        Assert.Single(prdb.AskedFor(Videos));
        Assert.Single(prdb.AskedFor(Batch));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = await context.CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("The First", video.Title);
        Assert.Equal("the first", video.NormalisedTitle);
        Assert.NotNull(video.SiteId);
        Assert.NotEqual(default, video.LastReadAt);

        // Everything the detail read brought, and the artwork above all: it is
        // the only thing that arrives this way and nowhere else.
        Assert.Equal(1, await context.CatalogueImages.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.CatalogueVideoActors.CountAsync(TestContext.Current.CancellationToken));

        var preName = await context.CatalogueVideoPreNames.SingleAsync(TestContext.Current.CancellationToken);

        // ADR 0023: a pre-name arriving after the cache was written has to be
        // looked for backwards, so it lands unsearched.
        Assert.False(preName.SearchedBackwards);
    }

    /// <summary>
    /// A video the catalogue already holds is not read again here. Correcting
    /// what is held is ADR 0013's repair pass, on a budget of its own, and doing
    /// it from this routine would put the whole catalogue's repair on a
    /// fifteen-minute cadence.
    /// </summary>
    [Fact]
    public async Task A_video_the_catalogue_already_holds_costs_no_detail_read()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, VideoPage([(First, Noon)]))
            .Answers(Batch, Details([(First, "The First")]));

        await using var database = await CreateAsync(prdb);

        await RunAsync<WhatsNewRoutine>(database);
        await RunAsync<WhatsNewRoutine>(database);

        Assert.Equal(2, prdb.AskedFor(Videos).Count);
        Assert.Single(prdb.AskedFor(Batch));
    }

    /// <summary>
    /// ADR 0013 bounds the backfill by a page count and not by a date window,
    /// because a window of days has an unpredictable cost and a window of pages
    /// has a stated one. It carries its position, resumes there after a restart,
    /// and retires without leaving a recurring row behind.
    /// </summary>
    [Fact]
    public async Task The_backfill_walks_pages_resumes_after_a_restart_and_retires()
    {
        var full = FullPage(Noon);

        var prdb = new FakePrdbApi().Answers(Videos, full).Answers(Batch, "[]");

        await using var database = await CreateAsync(prdb);

        await StartAsync(database);
        Assert.True(await HasRowAsync(database, WhatsNewBackfillRoutine.RoutineName));

        await RunAsync<WhatsNewBackfillRoutine>(database);

        Assert.Equal("1", Query(prdb.AskedFor(Videos)[0], "Page"));
        Assert.Equal("desc", Query(prdb.AskedFor(Videos)[0], "SortDirection"));

        // A restart neither duplicates the row nor moves the position.
        await StartAsync(database);
        Assert.True(await HasRowAsync(database, WhatsNewBackfillRoutine.RoutineName));

        await RunAsync<WhatsNewBackfillRoutine>(database);
        Assert.Equal("2", Query(prdb.AskedFor(Videos)[1], "Page"));

        // The rest of the ceiling, and then it is gone.
        for (var page = 3; page <= Backfill.LastPage; page++)
        {
            await RunAsync<WhatsNewBackfillRoutine>(database);
        }

        Assert.Equal(Backfill.LastPage, prdb.AskedFor(Videos).Count);
        Assert.False(await HasRowAsync(database, WhatsNewBackfillRoutine.RoutineName));

        // And a restart does not bring it back, so a bootstrap happens once.
        await StartAsync(database);

        Assert.False(await HasRowAsync(database, WhatsNewBackfillRoutine.RoutineName));
        Assert.True(await HasRowAsync(database, WhatsNewRoutine.RoutineName));
    }

    /// <summary>
    /// The cheaper of the two ways it finishes: a page short of what was asked
    /// for is the end of what prdb has, and reading nineteen more pages of
    /// nothing would be the ceiling doing the work of an answer already given.
    /// </summary>
    [Fact]
    public async Task The_backfill_retires_at_the_end_of_what_prdb_has()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, VideoPage([(First, Noon)]))
            .Answers(Batch, Details([(First, "The First")]));

        await using var database = await CreateAsync(prdb);

        await StartAsync(database);
        await RunAsync<WhatsNewBackfillRoutine>(database);

        Assert.Single(prdb.AskedFor(Videos));
        Assert.False(await HasRowAsync(database, WhatsNewBackfillRoutine.RoutineName));
    }

    /// <summary>
    /// The two run separately and neither can move the other's position: one
    /// walks a high-water mark forwards in the sync lane, the other walks pages
    /// backwards in the bulk one, and ADR 0033 gives them a cursor each.
    /// </summary>
    [Fact]
    public async Task The_backfill_and_the_recurring_routine_are_separate_routines()
    {
        var prdb = new FakePrdbApi()
            .Answers(Videos, VideoPage([(First, Noon)]))
            .Answers(Batch, Details([(First, "The First")]));

        await using var database = await CreateAsync(prdb);

        await StartAsync(database);

        await using var scope = database.Scope();

        var backfill = scope.ServiceProvider.GetRequiredService<WhatsNewBackfillRoutine>();
        var recurring = scope.ServiceProvider.GetRequiredService<WhatsNewRoutine>();

        Assert.Equal(Lane.Bulk, backfill.Lane);
        Assert.Equal(Lane.Sync, recurring.Lane);
        Assert.NotEqual(backfill.Name, recurring.Name);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddFabSync());

        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
            row => row.SetProperty(installation => installation.PrdbApiKey, ApiKey),
            TestContext.Current.CancellationToken);

        return database;
    }

    private static Task StartAsync(TestDatabase database) =>
        database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);

    private static async Task RunAsync<TRoutine>(TestDatabase database)
        where TRoutine : class, IRoutine
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<TRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static async Task<bool> HasRowAsync(TestDatabase database, string name)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .Routines
            .AnyAsync(row => row.Name == name, TestContext.Current.CancellationToken);
    }

    private static string? Query(Uri uri, string name) =>
        HttpUtility.ParseQueryString(uri.Query)[name];

    private static DateTimeOffset? Time(string? text) =>
        text is null
            ? null
            : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string VideoPage(IReadOnlyList<(Guid Id, DateTimeOffset Created)> videos) =>
        $$"""
        {
          "items": [
            {{string.Join(",", videos.Select(video => $$"""
            {
              "id": "{{video.Id}}",
              "title": "A Video",
              "siteId": "{{ASite}}",
              "siteTitle": "A Site",
              "createdAtUtc": "{{Stamp(video.Created)}}",
              "actors": []
            }
            """))}}
          ],
          "page": 1,
          "pageSize": 100,
          "totalCount": {{videos.Count}},
          "sortBy": "createdAtUtc",
          "sortDirection": "desc"
        }
        """;

    /// <summary>
    /// A page as long as what was asked for, which is what says there may be
    /// another behind it. The ids differ per row so that nothing is written
    /// twice by accident.
    /// </summary>
    private static string FullPage(DateTimeOffset created) =>
        VideoPage([.. Enumerable.Range(1, Backfill.APage).Select(index =>
            (Guid.Parse($"eeeeeeee-0000-4000-8000-{index:D12}"), created))]);

    private static string Details(IReadOnlyList<(Guid Id, string Title)> videos) =>
        $$"""
        [
          {{string.Join(",", videos.Select(video => $$"""
          {
            "id": "{{video.Id}}",
            "title": "{{video.Title}}",
            "updatedAtUtc": "{{Stamp(Noon)}}",
            "createdAtUtc": "{{Stamp(Noon)}}",
            "site": { "id": "{{ASite}}", "title": "A Site", "url": "https://example.invalid" },
            "actors": [ { "id": "{{AnActor}}", "name": "Jane Doe", "images": [] } ],
            "images": [ { "id": "{{ImageOf(video.Id)}}", "url": "https://example.invalid/a.jpg" } ],
            "preNames": [ { "id": "{{ImageOf(video.Id)}}", "title": "ASite.26.08.15.Jane.Doe.XXX" } ]
          }
          """))}}
        ]
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
