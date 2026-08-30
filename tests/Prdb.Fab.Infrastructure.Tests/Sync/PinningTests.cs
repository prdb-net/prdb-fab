using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// What is pinned, and what eviction may touch: ADR 0033's query rather than a
/// column, and the walk that is the reason a query is affordable.
/// </summary>
public sealed class PinningTests
{
    /// <summary>
    /// ADR 0015's rule, at the one place this slice can state it: a row
    /// something local points at is never dropped to hold a ceiling, and a row
    /// nothing points at is exactly what the ceiling is held with.
    /// </summary>
    [Fact]
    public async Task A_wanted_video_survives_an_eviction_that_takes_the_table_to_its_ceiling()
    {
        await using var database = await CreateAsync();

        var videos = await FillAsync(database, count: 6);

        // The oldest row, so that surviving is a decision rather than an
        // accident of the order eviction walks in.
        await WantAsync(database, videos[0]);

        var eviction = await EvictAsync(database, ceiling: 3);

        Assert.Equal(3, eviction.Removed);

        var left = await IdsAsync(database);

        Assert.Contains(videos[0], left);
        Assert.Equal(3, left.Count);

        // And the three that went are the oldest of the unpinned ones, which is
        // the first-seen order the walk is in.
        Assert.DoesNotContain(videos[1], left);
        Assert.DoesNotContain(videos[2], left);
        Assert.DoesNotContain(videos[3], left);
    }

    [Fact]
    public async Task A_recent_unpinned_video_survives_catalogue_eviction()
    {
        await using var database = await CreateAsync();
        var videos = await FillAsync(database, count: 4);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().CatalogueVideos
                .Where(row => row.Id == videos[0])
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.CreatedAtUtc, database.Time.GetUtcNow()),
                    TestContext.Current.CancellationToken);
        }

        var eviction = await EvictAsync(database, ceiling: 1);

        Assert.Equal(3, eviction.Removed);
        Assert.Equal([videos[0]], await IdsAsync(database));
    }

    /// <summary>
    /// The sentence ADR 0033 rested on when it corrected ADR 0013: eviction
    /// walks candidates in first-seen order rather than scanning the table, so
    /// the anti-join is evaluated over the rows it looks at. Asserted as the
    /// bound rather than measured as a duration — a timing is a property of the
    /// machine, and this is a property of the query.
    /// </summary>
    [Fact]
    public async Task Eviction_over_a_catalogue_at_its_ceiling_reads_a_bounded_number_of_rows()
    {
        await using var database = await CreateAsync();

        var videos = await FillAsync(database, CatalogueEviction.AWindow + 20);

        var eviction = await EvictAsync(database, CatalogueEviction.AWindow);

        Assert.Equal(videos.Count, eviction.Held);
        Assert.Equal(20, eviction.Removed);

        // One window, whatever the table holds.
        Assert.Equal(CatalogueEviction.AWindow, eviction.Examined);
        Assert.True(eviction.Examined < eviction.Held);
    }

    /// <summary>
    /// A catalogue already under its ceiling is not walked at all, which is the
    /// ordinary state and the one this costs a single count.
    /// </summary>
    [Fact]
    public async Task A_catalogue_under_its_ceiling_is_not_walked()
    {
        await using var database = await CreateAsync();

        await FillAsync(database, count: 3);

        var eviction = await EvictAsync(database, ceiling: 10);

        Assert.Equal(0, eviction.Removed);
        Assert.Equal(0, eviction.Examined);
    }

    /// <summary>
    /// ADR 0013 names six things that may point at a catalogue video and five of
    /// their tables do not exist yet. What this asserts is the shape rather than
    /// the row: a source registered beside the wanted list is asked, and adding
    /// it was one clause.
    /// </summary>
    [Fact]
    public async Task Adding_a_second_pinning_source_is_one_more_clause()
    {
        await using var database = await CreateAsync(also: services =>
            services.AddScoped<ICataloguePin, PinsWhateverHasArtwork>());

        var videos = await FillAsync(database, count: 4);

        // Nothing wants it; the second source is the only thing holding it.
        await GiveArtworkAsync(database, videos[0]);

        var eviction = await EvictAsync(database, ceiling: 1);

        Assert.Equal(3, eviction.Removed);
        Assert.Equal([videos[0]], await IdsAsync(database));
    }

    /// <summary>
    /// Diagnosis without a column: the clauses are named, so the query knows
    /// which of them matched. Nothing displays it yet — the answer exists.
    /// </summary>
    [Fact]
    public async Task Why_a_row_is_pinned_is_answerable_from_the_query()
    {
        await using var database = await CreateAsync(also: services =>
            services.AddScoped<ICataloguePin, PinsWhateverHasArtwork>());

        var videos = await FillAsync(database, count: 3);

        await WantAsync(database, videos[0]);
        await GiveArtworkAsync(database, videos[0]);
        await GiveArtworkAsync(database, videos[1]);

        await using var scope = database.Scope();
        var pins = scope.ServiceProvider.GetRequiredService<CataloguePins>();

        Assert.Equal(
            [PinReason.WantedVideo, PinReason.LibraryEntry],
            await pins.WhyAsync(videos[0], TestContext.Current.CancellationToken));

        Assert.Equal(
            [PinReason.LibraryEntry],
            await pins.WhyAsync(videos[1], TestContext.Current.CancellationToken));

        Assert.Empty(await pins.WhyAsync(videos[2], TestContext.Current.CancellationToken));

        Assert.True(await pins.IsPinnedAsync(videos[0], TestContext.Current.CancellationToken));
        Assert.False(await pins.IsPinnedAsync(videos[2], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Dropping a catalogue video takes what hangs off it, because ADR 0033
    /// declares the cascades and ADR 0039 opens SQLite with foreign keys on.
    /// The row count would hold either way; what would not is the artwork rows
    /// of a video nothing can reach.
    /// </summary>
    [Fact]
    public async Task Evicting_a_video_takes_its_artwork_with_it()
    {
        await using var database = await CreateAsync();

        var videos = await FillAsync(database, count: 2);

        await GiveArtworkAsync(database, videos[0]);
        await WantAsync(database, videos[1]);

        await EvictAsync(database, ceiling: 1);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(0, await context.CatalogueImages.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A source that stands in for one of the five tables that do not exist
    /// yet. It points at whatever has an image row, which is a fact this test
    /// can arrange and nothing else in the schema reads as a pin.
    /// </summary>
    private sealed class PinsWhateverHasArtwork(FabDbContext context) : ICataloguePin
    {
        public PinReason Reason => PinReason.LibraryEntry;

        public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
            video => context.CatalogueImages.Any(image => image.VideoId == video.Id);

        /// <summary>
        /// The image row has no stamp of its own, so this source has nothing to
        /// say about when it started pointing — which is a legitimate answer to
        /// the question and the one that keeps the order stable rather than
        /// inventing a time.
        /// </summary>
        public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince => _ => null;
    }

    private static Task<TestDatabase> CreateAsync(Action<IServiceCollection>? also = null) =>
        TestDatabase.CreateAsync(also: services =>
        {
            services.AddFabSync();
            also?.Invoke(services);
        });

    /// <summary>
    /// <paramref name="count"/> catalogue videos, in the order they were seen.
    /// Written straight rather than through a detail read: what is under test
    /// is which rows a walk takes, and a fixture that spent a request per row
    /// would say nothing more about it.
    /// </summary>
    private static async Task<IReadOnlyList<long>> FillAsync(TestDatabase database, int count)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var videos = Enumerable.Range(1, count)
            .Select(index => new CatalogueVideoRow
            {
                PrdbId = Guid.Parse($"aaaaaaaa-0000-4000-8000-{index:D12}"),
                Title = $"Video {index}",
                NormalisedTitle = $"video {index}",
            })
            .ToList();

        context.CatalogueVideos.AddRange(videos);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return [.. videos.Select(video => video.Id)];
    }

    private static async Task WantAsync(TestDatabase database, long videoId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.WantedVideos.Add(new WantedVideoRow { VideoId = videoId });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task GiveArtworkAsync(TestDatabase database, long videoId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.CatalogueImages.Add(new CatalogueImageRow
        {
            PrdbId = Guid.NewGuid(),
            VideoId = videoId,
            Url = "https://example.invalid/a.jpg",
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<Eviction> EvictAsync(TestDatabase database, int ceiling)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<CatalogueEviction>()
            .EvictAsync(ceiling, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<long>> IdsAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .CatalogueVideos
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
