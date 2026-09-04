using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class LibraryBrowseTests
{
    [Fact]
    public async Task Library_is_held_only_groups_qualities_and_shows_video_operations()
    {
        await using var database = await TestDatabase.CreateAsync();
        var held = Guid.NewGuid();
        var merelyCatalogued = Guid.NewGuid();
        long artworkId;
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var heldRow = new CatalogueVideoRow
            {
                PrdbId = held,
                Title = "Held Title",
                NormalisedTitle = "held title",
                DurationMs = 3_600_000,
                DurationSpreadMs = 2_000,
                DurationFileCount = 4,
            };
            context.CatalogueVideos.AddRange(
                heldRow,
                new CatalogueVideoRow
                {
                    PrdbId = merelyCatalogued,
                    Title = "Catalogue Only",
                    NormalisedTitle = "catalogue only",
                });
            context.LibraryEntries.Add(new LibraryEntryRow
            {
                VideoId = held,
                EntryDirectory = "/library/Held Title",
                FiledAt = database.Time.GetUtcNow(),
            });
            context.VideoFiles.AddRange(
                File(held, "1080p", "/library/Held Title/Held Title - [1080p].mkv"),
                File(held, "720p", "/library/Held Title/Held Title - [720p].mkv"));
            context.OperationLogEntries.Add(new OperationLogEntryRow
            {
                Id = Guid.NewGuid(),
                Act = "Filed",
                VideoId = held,
                PathAfter = "/library/Held Title/Held Title - [1080p].mkv",
                Actor = "Filing",
                Reason = "Test",
                At = database.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            artworkId = heldRow.Id;
        }

        await using var read = database.Scope();
        var browse = read.ServiceProvider.GetRequiredService<LibraryBrowse>();
        var page = await browse.ReadAsync(null, null, null, null, 1, TestContext.Current.CancellationToken);
        var card = Assert.Single(page.Entries);
        Assert.Equal(held, card.Id);
        Assert.Equal(artworkId, card.ArtworkId);
        Assert.Equal(["1080p", "720p"], card.Qualities);

        var entry = await browse.EntryAsync(held, TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.Equal(2, entry!.Files.Count);
        Assert.Equal(3_600_000, entry.ConsensusRuntimeMs);
        Assert.Equal("Filed", Assert.Single(entry.Operations.Entries).Act);
    }

    [Fact]
    public async Task Library_orders_titles_by_the_comparison_form_and_qualities_by_the_ladder()
    {
        await using var database = await TestDatabase.CreateAsync();
        var zebra = Guid.NewGuid();
        var apple = Guid.NewGuid();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.CatalogueVideos.AddRange(Video(zebra, "Zebra Crossing"), Video(apple, "apple orchard"));
            context.LibraryEntries.AddRange(Entry(zebra, database.Time.GetUtcNow()), Entry(apple, database.Time.GetUtcNow()));
            context.VideoFiles.AddRange(
                File(zebra, "720p", "/library/Zebra Crossing/Zebra Crossing - [720p].mkv", 1_800),
                File(zebra, "2160p", "/library/Zebra Crossing/Zebra Crossing - [2160p].mkv", 7_200),
                File(zebra, "1080p", "/library/Zebra Crossing/Zebra Crossing - [1080p].mkv", 3_600));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = database.Scope();
        var browse = read.ServiceProvider.GetRequiredService<LibraryBrowse>();
        var page = await browse.ReadAsync(null, null, null, null, 1, TestContext.Current.CancellationToken);

        // Not the title: SQLite's BINARY collation would put "Zebra Crossing"
        // first, because an upper case Z sorts below a lower case a.
        Assert.Equal(["apple orchard", "Zebra Crossing"], page.Entries.Select(entry => entry.Title));

        var card = page.Entries[1];
        Assert.Equal(["2160p", "1080p", "720p"], card.Qualities);
        Assert.Equal(7_200, card.RuntimeSeconds);
        Assert.Equal(["2160p", "1080p", "720p"], page.Filters.Qualities);

        var entry = await browse.EntryAsync(zebra, TestContext.Current.CancellationToken);
        Assert.NotNull(entry);
        Assert.Equal(["2160p", "1080p", "720p"], entry!.Files.Select(file => file.Quality));
    }

    [Fact]
    public async Task Operation_log_is_newest_first_and_filters_by_act_and_path()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.OperationLogEntries.AddRange(
                Operation("Filed", "/library/First.mkv", database.Time.GetUtcNow()),
                Operation("Deleted", "/downloads/Second.mkv", database.Time.GetUtcNow().AddMinutes(1)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = database.Scope();
        var log = read.ServiceProvider.GetRequiredService<OperationLogBrowse>();
        var all = await log.ReadAsync(null, null, null, 1, TestContext.Current.CancellationToken);
        Assert.Equal("Deleted", all.Entries[0].Act);
        var filtered = await log.ReadAsync("Filed", "First.mkv", null, 1, TestContext.Current.CancellationToken);
        Assert.Equal("Filed", Assert.Single(filtered.Entries).Act);
    }

    private static CatalogueVideoRow Video(Guid prdbId, string title) => new()
    {
        PrdbId = prdbId,
        Title = title,
        NormalisedTitle = ComparisonForm.Of(title),
    };

    private static LibraryEntryRow Entry(Guid video, DateTimeOffset filedAt) => new()
    {
        VideoId = video,
        EntryDirectory = "/library/" + video,
        FiledAt = filedAt,
    };

    private static VideoFileRow File(Guid video, string quality, string path, long runtime = 3_600) => new()
    {
        Id = Guid.NewGuid(),
        LibraryEntryVideoId = video,
        FiledPath = path,
        QualityLabel = quality,
        SizeBytes = 100,
        RuntimeSeconds = runtime,
    };

    private static OperationLogEntryRow Operation(string act, string path, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        Act = act,
        PathBefore = path,
        Actor = "Test",
        Reason = "Test",
        At = at,
    };
}
