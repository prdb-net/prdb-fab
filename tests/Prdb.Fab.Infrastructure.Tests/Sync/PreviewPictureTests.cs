using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0060's one request: a Preview opened on a Video the Catalogue holds no
/// picture of, and everything that keeps it to one.
/// </summary>
public sealed class PreviewPictureTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Batch = "/videos/batch";
    private static readonly Guid Video = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Site = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly Guid Picture = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Noon = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The whole path: a Video the list feed wrote and nothing has read in
    /// detail, a person opening its Preview, one request, and a gallery where
    /// there was none.
    /// </summary>
    [Fact]
    public async Task A_preview_with_no_pictures_asks_prdb_and_gets_them()
    {
        var prdb = new FakePrdbApi().Answers(Batch, Details(withPicture: true));

        await using var database = await CreateAsync(prdb, neverRead: true);

        Assert.Equal(PreviewPictureOutcome.Asked, await AskAsync(database));

        // Nothing is fetched in the ask itself: the sheet does not wait on prdb.
        Assert.Empty(prdb.AskedFor(Batch));

        await RunAsync(database);

        Assert.Single(prdb.AskedFor(Batch));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(Picture, (await context.CatalogueImages.SingleAsync(
            TestContext.Current.CancellationToken)).PrdbId);

        // The row retires, which is what makes the next ask a fresh decision
        // rather than a second request.
        Assert.False(await context.Routines.AnyAsync(
            row => row.Name == PreviewPictureRoutine.RoutineName,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Reopening the same sheet a hundred times is one request. While the read
    /// is outstanding the routine row is the record of it, so a second ask adds
    /// nothing.
    /// </summary>
    [Fact]
    public async Task Asking_twice_schedules_once()
    {
        await using var database = await CreateAsync(new FakePrdbApi(), neverRead: true);

        Assert.Equal(PreviewPictureOutcome.Asked, await AskAsync(database));
        Assert.Equal(PreviewPictureOutcome.Asked, await AskAsync(database));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Single(await context.Routines
            .Where(row => row.Name == PreviewPictureRoutine.RoutineName)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A Video read in detail recently and still carrying no picture is a Video
    /// prdb publishes none of. Asking again would spend a request to learn what
    /// is already known, and there is no second column saying so — the stamp
    /// the detail read leaves is the record.
    /// </summary>
    [Fact]
    public async Task A_video_read_recently_is_taken_at_its_word()
    {
        await using var database = await CreateAsync(new FakePrdbApi(), neverRead: false);

        Assert.Equal(PreviewPictureOutcome.PrdbPublishesNone, await AskAsync(database));

        await using var scope = database.Scope();

        Assert.False(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Routines.AnyAsync(
                row => row.Name == PreviewPictureRoutine.RoutineName,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A week later the same Video may be asked about again — the image feed is
    /// what normally brings a picture that arrived after the row was written,
    /// and this is the backstop for the one whose cursor was already past.
    /// </summary>
    [Fact]
    public async Task A_video_read_long_ago_may_be_asked_about_again()
    {
        await using var database = await CreateAsync(new FakePrdbApi(), neverRead: false);

        database.Time.Advance(PreviewPictures.Recently + TimeSpan.FromMinutes(1));

        Assert.Equal(PreviewPictureOutcome.Asked, await AskAsync(database));
    }

    /// <summary>
    /// A Video that has a gallery already. Whether its pictures are the newest
    /// prdb has is the repair pass's question rather than a Preview's.
    /// </summary>
    [Fact]
    public async Task A_video_that_has_pictures_is_refused()
    {
        await using var database = await CreateAsync(new FakePrdbApi(), neverRead: true);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var videoId = await context.CatalogueVideos
                .Select(row => row.Id)
                .SingleAsync(TestContext.Current.CancellationToken);

            context.CatalogueImages.Add(new CatalogueImageRow
            {
                PrdbId = Picture,
                VideoId = videoId,
                Url = "https://cdn.example/one.png",
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(PreviewPictureOutcome.AlreadyKnown, await AskAsync(database));
    }

    /// <summary>
    /// prdb answering with no picture is an answer. The row retires, the stamp
    /// moves, and the next Preview of this Video spends nothing.
    /// </summary>
    [Fact]
    public async Task An_answer_of_no_pictures_stops_the_asking()
    {
        var prdb = new FakePrdbApi().Answers(Batch, Details(withPicture: false));

        await using var database = await CreateAsync(prdb, neverRead: true);

        Assert.Equal(PreviewPictureOutcome.Asked, await AskAsync(database));

        await RunAsync(database);

        Assert.Empty(await ImagesAsync(database));
        Assert.Equal(PreviewPictureOutcome.PrdbPublishesNone, await AskAsync(database));
        Assert.Single(prdb.AskedFor(Batch));
    }

    /// <summary>
    /// Nothing is created at startup. Every row of this routine is a person
    /// opening a Preview, so a build that knows about the routine must not give
    /// it one.
    /// </summary>
    [Fact]
    public async Task The_registrar_creates_no_row_for_it()
    {
        await using var database = await CreateAsync(new FakePrdbApi(), neverRead: true);

        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<RoutineRegistrar>()
            .EnsureRowsExistAsync(TestContext.Current.CancellationToken);

        Assert.False(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Routines.AnyAsync(
                row => row.Name == PreviewPictureRoutine.RoutineName,
                TestContext.Current.CancellationToken));
    }

    private static async Task<PreviewPictureOutcome> AskAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return (await scope.ServiceProvider.GetRequiredService<PreviewPictures>()
            .AskAsync(Video, TestContext.Current.CancellationToken)).Outcome;
    }

    private static async Task RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<PreviewPictureRoutine>()
            .RunAsync(Video.ToString("D"), TestContext.Current.CancellationToken);
    }

    private static async Task<List<Guid>> ImagesAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .CatalogueImages
            .Select(row => row.PrdbId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One Catalogue Video with no pictures, either never read in detail — the
    /// row a list feed writes — or read a moment ago.
    /// </summary>
    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb, bool neverRead)
    {
        var database = await TestDatabase.CreateAsync(prdb: prdb, also: services => services.AddFabSync());

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        await context.Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.PrdbApiKey, ApiKey),
            TestContext.Current.CancellationToken);

        context.CatalogueVideos.Add(new CatalogueVideoRow
        {
            PrdbId = Video,
            Title = "A scene",
            NormalisedTitle = "a scene",
            LastReadAt = neverRead ? default : database.Time.GetUtcNow(),
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return database;
    }

    private static string Details(bool withPicture) =>
        $$"""
        [{
          "id":"{{Video}}", "title":"A scene", "createdAtUtc":"{{Stamp(Noon)}}", "updatedAtUtc":"{{Stamp(Noon)}}",
          "site":{"id":"{{Site}}","title":"A Site"},
          "actors":[], "preNames":[],
          "images":[{{(withPicture ? $$"""{"id":"{{Picture}}","url":"https://cdn.example/one.png"}""" : "")}}]
        }]
        """;

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
