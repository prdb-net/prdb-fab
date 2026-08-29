using System.Globalization;
using System.Xml.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0013's repair pass: the two holes one read closes, over pinned rows
/// only, on a budget rather than a cadence.
/// </summary>
public sealed class RepairTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string Batch = "/videos/batch";
    private const string Sites = "/sites";

    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid First = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid Third = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000003");

    private static readonly Guid ASite = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid AnActor = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    private static Guid ImageOf(Guid video) => Guid.Parse("dddddddd" + video.ToString("D")[8..]);

    /// <summary>
    /// The hole no feed can close. prdb hard-deletes image rows, and its images
    /// feed is documented as never emitting a deletion — so an image that has
    /// stopped being in <c>images[]</c> is one that has been removed, and this
    /// pass is the only place that is ever noticed.
    /// </summary>
    [Fact]
    public async Task An_image_removed_upstream_disappears_on_the_next_pass()
    {
        var prdb = new FakePrdbApi().Answers(Batch, Details([(First, "A Video", Images: false)]));

        await using var database = await CreateAsync(prdb);

        var video = await HoldAsync(database, First, "A Video");
        await GiveArtworkAsync(database, video, ImageOf(First));
        await WantAsync(database, video);

        await RepairAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.CatalogueImages.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The other hole: video metadata edits have no feed at all. The same read
    /// finds the correction, which is why ADR 0013 makes this one pass and not
    /// two — and a corrected title is a new needle, so ADR 0023's flag goes back
    /// to <em>not yet searched</em> with it.
    /// </summary>
    [Fact]
    public async Task A_title_edited_upstream_is_corrected_and_the_last_re_read_moves()
    {
        var prdb = new FakePrdbApi().Answers(Batch, Details([(First, "The Corrected Title", Images: true)]));

        await using var database = await CreateAsync(prdb);

        var video = await HoldAsync(database, First, "The Old Title", lastReadAt: Noon, searched: true);
        await WantAsync(database, video);

        await RepairAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var row = await context.CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("The Corrected Title", row.Title);
        Assert.Equal("the corrected title", row.NormalisedTitle);
        Assert.False(row.TitleSearchedBackwards);

        // The stamp the pass walks in the order of. It has moved to the clock
        // the test holds, so the next pass sorts this row behind whatever has
        // not been read yet.
        Assert.Equal(database.Time.GetUtcNow(), row.LastReadAt);
    }

    [Fact]
    public async Task A_repair_refreshes_held_sidecar_and_changed_cached_image_without_renaming_video()
    {
        var root = Path.Combine(Path.GetTempPath(), "prdb-fab-repair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var filedPath = Path.Combine(root, "Recorded Name.mkv");
        await File.WriteAllBytesAsync(filedPath, [1, 2, 3], TestContext.Current.CancellationToken);
        var oldImage = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
        var newImage = ImageOf(First);

        try
        {
            var prdb = new FakePrdbApi().Answers(Batch, Details([(First, "The Corrected Title", Images: true)]));
            await using var database = await CreateAsync(prdb);
            var video = await HoldAsync(database, First, "The Old Title");
            await GiveArtworkAsync(database, video, oldImage);

            await using (var scope = database.Scope())
            {
                var store = scope.ServiceProvider.GetRequiredService<ArtworkStore>();
                await store.WriteAsync(oldImage, [1, 1, 1], TestContext.Current.CancellationToken);
                await store.WriteAsync(newImage, [2, 2, 2], TestContext.Current.CancellationToken);
                var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
                context.LibraryEntries.Add(new LibraryEntryRow
                {
                    VideoId = First,
                    EntryDirectory = root,
                    FiledAt = database.Time.GetUtcNow(),
                });
                context.VideoFiles.Add(new VideoFileRow
                {
                    Id = Guid.NewGuid(),
                    LibraryEntryVideoId = First,
                    FiledPath = filedPath,
                    QualityLabel = "1080p",
                    SizeBytes = 3,
                });
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                await scope.ServiceProvider.GetRequiredService<EntryFiles>()
                    .WriteAsync(root, First, TestContext.Current.CancellationToken);
            }

            await RepairAsync(database);

            var sidecar = XDocument.Load(Path.Combine(root, EntryPath.SidecarFileName));
            Assert.Equal("The Corrected Title", sidecar.Root!.Element("title")!.Value);
            Assert.Equal([2, 2, 2], await File.ReadAllBytesAsync(
                Path.Combine(root, EntryPath.EntryImageFileName),
                TestContext.Current.CancellationToken));
            await using var check = database.Scope();
            Assert.Equal(filedPath, (await check.ServiceProvider.GetRequiredService<FabDbContext>()
                .VideoFiles.SingleAsync(TestContext.Current.CancellationToken)).FiledPath);
            Assert.True(File.Exists(filedPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Oldest-checked first, which is what makes <em>the next pass takes a
    /// different fifty</em> true rather than hoped for. The ids in the request
    /// are the only record of what a pass decided to ask about.
    /// </summary>
    [Fact]
    public async Task The_pass_asks_about_the_least_recently_read_rows_first()
    {
        var prdb = new FakePrdbApi().Answers(Batch, "[]");

        await using var database = await CreateAsync(prdb);

        // Deliberately not in the order they were written: the walk is by the
        // stamp, and an id tie-break would hide that.
        await WantAsync(database, await HoldAsync(database, First, "First", lastReadAt: Noon));
        await WantAsync(database, await HoldAsync(database, Second, "Second", lastReadAt: Noon.AddHours(-2)));
        await WantAsync(database, await HoldAsync(database, Third, "Third", lastReadAt: Noon.AddHours(-1)));

        await RepairAsync(database);

        var body = Assert.Single(prdb.AskingFor(Batch)).Body;

        Assert.True(body.IndexOf(Second.ToString(), StringComparison.Ordinal)
            < body.IndexOf(Third.ToString(), StringComparison.Ordinal));

        Assert.True(body.IndexOf(Third.ToString(), StringComparison.Ordinal)
            < body.IndexOf(First.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// ADR 0013 accepts this by name: an unpinned row's artwork URL may be dead
    /// before it is evicted, a missing image on a browse grid is the cost, and
    /// a pinned row is never in that state. Repairing everything would be the
    /// unbounded obligation the cache exists to avoid.
    /// </summary>
    [Fact]
    public async Task An_unpinned_row_is_never_repaired()
    {
        var prdb = new FakePrdbApi().Answers(Batch, "[]");

        await using var database = await CreateAsync(prdb);

        await WantAsync(database, await HoldAsync(database, First, "Wanted"));
        await HoldAsync(database, Second, "Merely Held");

        await RepairAsync(database);

        var body = Assert.Single(prdb.AskingFor(Batch)).Body;

        Assert.Contains(First.ToString(), body, StringComparison.Ordinal);
        Assert.DoesNotContain(Second.ToString(), body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A catalogue with nothing pinned in it is a work set that is empty, and
    /// ADR 0032 says an empty tick is not a run — so it spends no request and
    /// records nothing.
    /// </summary>
    [Fact]
    public async Task A_catalogue_with_nothing_pinned_spends_no_request()
    {
        var prdb = new FakePrdbApi().Answers(Batch, "[]");

        await using var database = await CreateAsync(prdb);

        await HoldAsync(database, First, "Merely Held");

        Assert.Same(RunResult.NothingToDo, await RepairAsync(database));
        Assert.Empty(prdb.AskingFor(Batch));
    }

    /// <summary>
    /// ADR 0014's number, at the plan that makes it bite. Repair is last in the
    /// order of precedence, so an allowance computed down to zero would stall it
    /// forever without anything failing — and the five feeds above it are the
    /// half of the sentence that must stay true while it does not.
    /// </summary>
    [Fact]
    public async Task A_limit_of_ten_an_hour_still_buys_a_request_and_leaves_the_feeds_running()
    {
        var prdb = new FakePrdbApi { Hourly = (Limit: 10, Remaining: 10, ResetInSeconds: 3600) }
            .Answers(Batch, Details([(First, "A Video", Images: true)]))
            .Answers(Sites, """{"items":[],"page":1,"pageSize":1000,"totalCount":0}""");

        await using var database = await CreateAsync(prdb);

        await WantAsync(database, await HoldAsync(database, First, "A Video"));

        await RepairAsync(database);

        Assert.Single(prdb.AskingFor(Batch));

        // And the site list — the lowest of the five feeds in ADR 0014's order,
        // so the one repair would starve first if it were spending above its
        // share — still goes out afterwards.
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<SiteListRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
        }

        Assert.Single(prdb.AskingFor(Sites));

        var budget = database.Services.GetRequiredService<PrdbGovernor>().LastReading;

        Assert.NotNull(budget);
        Assert.True(budget.Admits(PrdbWork.UserFeeds));
        Assert.True(budget.Admits(PrdbWork.WhatsNew));
        Assert.True(budget.Admits(PrdbWork.Images));
        Assert.True(budget.Admits(PrdbWork.Actors));
        Assert.True(budget.Admits(PrdbWork.Sites));

        // Five requests above the half line, which is what the next run spends.
        Assert.Equal(5, RepairBudget.RequestsFor(budget));
    }

    /// <summary>
    /// ADR 0023: a pre-name arriving from a repair read is a new fact arriving
    /// from the other direction, and it has to land unsearched or ADR 0025's
    /// pass will never look at it — a row that sits unsearched with no error and
    /// no Gap is ADR 0015's silently skipped row one layer up.
    /// </summary>
    [Fact]
    public async Task A_pre_name_a_repair_read_brings_lands_unsearched()
    {
        var prdb = new FakePrdbApi().Answers(Batch, Details([(First, "A Video", Images: true)]));

        await using var database = await CreateAsync(prdb);

        await WantAsync(database, await HoldAsync(database, First, "A Video"));

        await RepairAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var preName = await context.CatalogueVideoPreNames
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.False(preName.SearchedBackwards);
    }

    /// <summary>
    /// The three figures ADR 0031 stores and no check reads. They arrive with
    /// the same read as everything else, and this is where a pinned row gets
    /// them — the summary a browse page was built from carries none.
    /// </summary>
    [Fact]
    public async Task The_consensus_runtime_arrives_with_the_repair_read()
    {
        var prdb = new FakePrdbApi().Answers(Batch, Details([(First, "A Video", Images: true)]));

        await using var database = await CreateAsync(prdb);

        await WantAsync(database, await HoldAsync(database, First, "A Video"));

        await RepairAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var row = await context.CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1_800_000, row.DurationMs);
        Assert.Equal(4_000, row.DurationSpreadMs);
        Assert.Equal(7, row.DurationFileCount);
    }

    /// <summary>
    /// prdb omits ids it does not know rather than refusing them, which is what
    /// makes it safe to ask about whatever is pinned. What is not safe is
    /// leaving such a row at the front of an oldest-first walk: it would be the
    /// same fifty every pass, forever, and nothing would say so.
    /// </summary>
    [Fact]
    public async Task A_row_prdb_no_longer_knows_does_not_hold_up_the_walk()
    {
        var prdb = new FakePrdbApi().Answers(Batch, "[]");

        await using var database = await CreateAsync(prdb);

        var video = await HoldAsync(database, First, "A Video", lastReadAt: Noon.AddYears(-1));
        await WantAsync(database, video);

        await RepairAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var row = await context.CatalogueVideos.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(database.Time.GetUtcNow(), row.LastReadAt);

        // And nothing was learned about it, so nothing about it changed.
        Assert.Equal("A Video", row.Title);
    }

    /// <summary>
    /// ADR 0014 puts the repair pass in the bulk lane, behind everything a
    /// person or an arrived file is waiting on.
    /// </summary>
    [Fact]
    public async Task The_repair_pass_runs_in_the_bulk_lane()
    {
        await using var database = await CreateAsync(new FakePrdbApi());

        await using var scope = database.Scope();

        Assert.Equal(
            Lane.Bulk,
            scope.ServiceProvider.GetRequiredService<CatalogueRepairRoutine>().Lane);
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

    private static async Task<RunResult> RepairAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<CatalogueRepairRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A catalogue row as an earlier pass left it. Written straight, because
    /// what is under test is what the <em>next</em> read does to it.
    /// </summary>
    private static async Task<long> HoldAsync(
        TestDatabase database,
        Guid prdbId,
        string title,
        DateTimeOffset? lastReadAt = null,
        bool searched = false)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var row = new CatalogueVideoRow
        {
            PrdbId = prdbId,
            Title = title,
            NormalisedTitle = title.ToLowerInvariant(),
            LastReadAt = lastReadAt ?? default,
            TitleSearchedBackwards = searched,
        };

        context.CatalogueVideos.Add(row);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return row.Id;
    }

    private static async Task WantAsync(TestDatabase database, long videoId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.WantedVideos.Add(new WantedVideoRow { VideoId = videoId, SinceAt = Noon });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task GiveArtworkAsync(TestDatabase database, long videoId, Guid imageId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.CatalogueImages.Add(new CatalogueImageRow
        {
            PrdbId = imageId,
            VideoId = videoId,
            Url = "https://example.invalid/a.jpg",
            Cached = true,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What <c>POST /videos/batch</c> answers with. <c>images</c> is the field
    /// the whole pass turns on, so a fixture can leave it empty — which is what
    /// a hard-deleted image looks like from here.
    /// </summary>
    private static string Details(IReadOnlyList<(Guid Id, string Title, bool Images)> videos) =>
        $$"""
        [
          {{string.Join(",", videos.Select(video => $$"""
          {
            "id": "{{video.Id}}",
            "title": "{{video.Title}}",
            "updatedAtUtc": "{{Stamp(Noon)}}",
            "createdAtUtc": "{{Stamp(Noon)}}",
            "durationMs": 1800000,
            "durationSpreadMs": 4000,
            "durationFileCount": 7,
            "site": { "id": "{{ASite}}", "title": "A Site", "url": "https://example.invalid" },
            "actors": [ { "id": "{{AnActor}}", "name": "Jane Doe", "images": [] } ],
            "images": [{{(video.Images
                ? $$"""{ "id": "{{ImageOf(video.Id)}}", "url": "https://example.invalid/a.jpg" }"""
                : string.Empty)}}],
            "preNames": [ { "id": "{{ImageOf(video.Id)}}", "title": "ASite.26.08.15.Jane.Doe.XXX" } ]
          }
          """))}}
        ]
        """;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
