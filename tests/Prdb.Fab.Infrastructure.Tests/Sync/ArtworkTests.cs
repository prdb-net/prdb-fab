using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0030's artwork cache: two populations, one file per video named by the
/// image's id, a ceiling over the disposable half and a dead URL marked once.
/// </summary>
public sealed class ArtworkTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static Guid Video(int number) =>
        Guid.Parse($"aaaaaaaa-0000-4000-8000-{number:D12}");

    private static Guid Image(int number) =>
        Guid.Parse($"dddddddd-0000-4000-8000-{number:D12}");

    private static string Url(int number) => $"https://cdn.example/{Image(number):n}.png";

    /// <summary>
    /// The first of ADR 0030's two triggers, and the one ADR 0027 requires: a
    /// held video's image has to be on disk before filing looks for it, so the
    /// routine puts it there rather than the display path.
    /// </summary>
    [Fact]
    public async Task A_pinned_videos_image_is_cached_without_anybody_looking_at_a_grid()
    {
        var cdn = new FakeCdn().Serves(Url(1));

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, video, Image(1), Url(1));
        await WantAsync(database, video, Noon);

        var run = await RunAsync(database);

        Assert.Equal(1, run.ItemsHandled);
        Assert.Single(cdn.Asked, Url(1));

        Assert.True(Store(database).Holds(Image(1)));
        Assert.True(await IsCachedAsync(database, Image(1)));
    }

    /// <summary>
    /// ADR 0030 takes newly pinned videos first, which is what puts a freshly
    /// downloaded video's image on disk while the copy that produced it is
    /// still running.
    /// </summary>
    [Fact]
    public async Task Newly_pinned_videos_are_taken_before_the_backlog()
    {
        var cdn = new FakeCdn().Serves(Url(1)).Serves(Url(2)).Serves(Url(3));

        await using var database = await CreateAsync(cdn);

        // Wanted longest ago first, so that the order they are fetched in is a
        // decision rather than the order the rows were written.
        foreach (var (number, wantedFor) in new[] { (1, 3), (2, 1), (3, 2) })
        {
            var video = await HoldAsync(database, Video(number));

            await GiveArtworkAsync(database, video, Image(number), Url(number));
            await WantAsync(database, video, Noon.AddDays(-wantedFor));
        }

        await RunAsync(database);

        Assert.Equal([Url(2), Url(3), Url(1)], cdn.Asked);
    }

    /// <summary>
    /// The second trigger, and the property <c>VISION.md</c> is buying: the
    /// grid asks the tool rather than the CDN, and the second scroll is free.
    /// </summary>
    [Fact]
    public async Task Asking_twice_for_an_unpinned_videos_image_makes_one_request()
    {
        var cdn = new FakeCdn().Serves(Url(1));

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, video, Image(1), Url(1));

        Assert.NotNull(await ServeAsync(database, video));
        Assert.NotNull(await ServeAsync(database, video));

        Assert.Equal(1, cdn.Requests);
    }

    [Fact]
    public async Task An_actor_profile_is_served_from_the_same_bounded_cache_after_one_fetch()
    {
        var cdn = new FakeCdn().Serves(Url(1));
        await using var database = await CreateAsync(cdn);
        var actorId = Guid.NewGuid();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.CatalogueActors.Add(new CatalogueActorRow
            {
                PrdbId = actorId,
                Name = "Actor",
                ProfileImageUrl = Url(1),
                ArtworkCacheKey = Image(1),
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.NotNull(await ServeActorAsync(database, actorId));
        Assert.NotNull(await ServeActorAsync(database, actorId));
        Assert.Equal(1, cdn.Requests);
        Assert.Equal(0, (await SweepAsync(database, long.MaxValue)).Orphans);
    }

    /// <summary>
    /// Nothing prefetches an unpinned video's artwork. The four browse surfaces
    /// range over a catalogue nobody scrolls all of, and ADR 0030 rejected
    /// filling it ahead of them by name.
    /// </summary>
    [Fact]
    public async Task The_routine_leaves_an_unpinned_video_alone()
    {
        var cdn = new FakeCdn().Serves(Url(1));

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, video, Image(1), Url(1));

        var run = await RunAsync(database);

        Assert.Null(run.Outcome);
        Assert.Equal(0, cdn.Requests);
    }

    /// <summary>
    /// ADR 0027's choice, which ADR 0030 caches rather than making again: the
    /// first entry of <c>images[]</c> carrying a URL, and no other.
    /// </summary>
    [Fact]
    public async Task The_image_fetched_is_the_first_one_carrying_a_url()
    {
        var cdn = new FakeCdn().Serves(Url(2)).Serves(Url(3));

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));

        // The oldest entry has no URL at all, so the choice falls to the next
        // one — and the third is never asked for.
        await GiveArtworkAsync(database, video, Image(1), url: string.Empty, position: 0);
        await GiveArtworkAsync(database, video, Image(2), Url(2), position: 1);
        await GiveArtworkAsync(database, video, Image(3), Url(3), position: 2);

        await WantAsync(database, video, Noon);

        await RunAsync(database);

        Assert.Single(cdn.Asked, Url(2));
    }

    /// <summary>
    /// ADR 0030: marked once, and no routine and no display asks again. prdb
    /// hard-deletes image rows, so a 404 is normally permanent.
    /// </summary>
    [Fact]
    public async Task A_dead_url_is_marked_once_and_never_asked_for_again()
    {
        var cdn = new FakeCdn().Lost(Url(1));

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, video, Image(1), Url(1));
        await WantAsync(database, video, Noon);

        await RunAsync(database);

        Assert.True(await IsDeadAsync(database, Image(1)));

        // Neither trigger asks a second time: not the routine, and not a grid.
        await RunAsync(database);

        Assert.Null(await ServeAsync(database, video));

        Assert.Equal(1, cdn.Requests);
    }

    /// <summary>
    /// The other half of the same rule. A transport failure is not a dead URL —
    /// collapsing the two would turn one flaky minute into a grid of permanent
    /// blanks.
    /// </summary>
    [Fact]
    public async Task A_refusal_that_is_not_a_404_leaves_no_mark()
    {
        // Nothing is arranged for this URL, so the fake answers 500.
        var cdn = new FakeCdn();

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, video, Image(1), Url(1));
        await WantAsync(database, video, Noon);

        await RunAsync(database);

        Assert.False(await IsDeadAsync(database, Image(1)));
        Assert.False(await IsCachedAsync(database, Image(1)));

        // And it is asked again, because nothing has been established about it.
        cdn.Serves(Url(1));

        await RunAsync(database);

        Assert.True(await IsCachedAsync(database, Image(1)));
    }

    /// <summary>
    /// ADR 0030's ceiling: bytes rather than rows, over the unpinned half only,
    /// least recently served first.
    /// </summary>
    [Fact]
    public async Task The_cache_over_its_ceiling_evicts_unpinned_files_by_last_served()
    {
        var cdn = new FakeCdn().Serves(Url(1), 1024).Serves(Url(2), 1024).Serves(Url(3), 1024);

        await using var database = await CreateAsync(cdn);

        var videos = new List<long>();

        foreach (var number in new[] { 1, 2, 3 })
        {
            var video = await HoldAsync(database, Video(number));

            await GiveArtworkAsync(database, video, Image(number), Url(number));

            videos.Add(video);
        }

        // Served oldest first, and the clock moves between them so that the
        // order is a fact on the rows rather than a tie.
        foreach (var video in videos)
        {
            Assert.NotNull(await ServeAsync(database, video));

            database.Time.Advance(TimeSpan.FromMinutes(1));
        }

        // Room for two of the three.
        var swept = await SweepAsync(database, ceiling: 2048);

        Assert.Equal(1, swept.Evicted);

        Assert.False(Store(database).Holds(Image(1)));
        Assert.True(Store(database).Holds(Image(2)));
        Assert.True(Store(database).Holds(Image(3)));

        // The row survives its bytes: it is prdb's record of the image, and only
        // the cached mark was ever about the file.
        Assert.False(await IsCachedAsync(database, Image(1)));
    }

    /// <summary>
    /// The half the ceiling does not bound. Pinned images are the library grid
    /// and the file filing copies from, so evicting one would mean a held video
    /// with no picture and a routine fetching it straight back.
    /// </summary>
    [Fact]
    public async Task Eviction_leaves_every_pinned_file_alone_and_does_not_count_it()
    {
        var cdn = new FakeCdn().Serves(Url(1), 4096).Serves(Url(2), 1024);

        await using var database = await CreateAsync(cdn);

        var pinned = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, pinned, Image(1), Url(1));
        await WantAsync(database, pinned, Noon);

        var browsed = await HoldAsync(database, Video(2));
        await GiveArtworkAsync(database, browsed, Image(2), Url(2));

        await RunAsync(database);

        Assert.NotNull(await ServeAsync(database, browsed));

        // A ceiling the pinned file alone would blow through four times over.
        var swept = await SweepAsync(database, ceiling: 2048);

        Assert.Equal(0, swept.Evicted);
        Assert.Equal(1, swept.Pinned);

        // What it weighed is the unpinned half, which is under the ceiling: the
        // pinned file is not counted rather than counted and spared.
        Assert.Equal(1024, swept.UnpinnedBytes);

        Assert.True(Store(database).Holds(Image(1)));
        Assert.True(Store(database).Holds(Image(2)));
    }

    /// <summary>
    /// ADR 0030 puts the artwork work set and ADR 0033's catalogue eviction in
    /// one routine, and this is why it is one: a catalogue row dropped takes its
    /// image rows with it by cascade, and the bytes they leave are swept in the
    /// same pass rather than the next.
    /// </summary>
    [Fact]
    public async Task The_routine_sweeps_up_after_the_catalogue_eviction_it_runs()
    {
        var cdn = new FakeCdn().Serves(Url(1));

        await using var database = await CreateAsync(cdn);

        var video = await HoldAsync(database, Video(1));
        await GiveArtworkAsync(database, video, Image(1), Url(1));

        Assert.NotNull(await ServeAsync(database, video));
        Assert.True(Store(database).Holds(Image(1)));

        // Nothing points at the video, so a ceiling of zero takes it — and the
        // image row with it.
        await using (var scope = database.Scope())
        {
            var eviction = scope.ServiceProvider.GetRequiredService<CatalogueEviction>();

            await eviction.EvictAsync(ceiling: 0, TestContext.Current.CancellationToken);
        }

        var run = await RunAsync(database);

        Assert.Equal(1, run.ItemsHandled);
        Assert.False(Store(database).Holds(Image(1)));
    }

    /// <summary>
    /// ADR 0032, at the one place this routine has to state it: an empty work
    /// set is not a run, so a tick with nothing to fetch, nothing over a ceiling
    /// and nothing left behind is not recorded and moves no counter.
    /// </summary>
    [Fact]
    public async Task A_tick_with_nothing_to_do_is_not_a_run()
    {
        await using var database = await CreateAsync(new FakeCdn());

        var run = await RunAsync(database);

        Assert.Null(run.Outcome);
        Assert.False(run.IsRecorded);
    }

    private static Task<TestDatabase> CreateAsync(FakeCdn cdn) =>
        TestDatabase.CreateAsync(also: services =>
        {
            services.AddFabSync();

            // ADR 0042: the socket, and everything above it is the real
            // transport — its timeout, its user agent, and the one redirect
            // rule that differs from the other three.
            services.AddHttpClient(FabTransports.Artwork)
                .ConfigurePrimaryHttpMessageHandler(() => cdn);
        });

    private static ArtworkStore Store(TestDatabase database) =>
        database.Services.CreateScope().ServiceProvider.GetRequiredService<ArtworkStore>();

    private static async Task<Prdb.Fab.Core.Scheduling.RunResult> RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<ArtworkRoutine>()
            .RunAsync(target: null, TestContext.Current.CancellationToken);
    }

    private static async Task<ArtworkSweep> SweepAsync(TestDatabase database, long ceiling)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<ArtworkEviction>()
            .SweepAsync(ceiling, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The display path, disposing what it was given the way the endpoint's
    /// response does.
    /// </summary>
    private static async Task<string?> ServeAsync(TestDatabase database, long videoId)
    {
        await using var scope = database.Scope();

        var served = await scope.ServiceProvider
            .GetRequiredService<ArtworkCache>()
            .ServeAsync(videoId, TestContext.Current.CancellationToken);

        if (served is null)
        {
            return null;
        }

        await served.Bytes.DisposeAsync();

        return served.MediaType;
    }

    private static async Task<string?> ServeActorAsync(TestDatabase database, Guid actorId)
    {
        await using var scope = database.Scope();
        var served = await scope.ServiceProvider
            .GetRequiredService<ActorArtworkCache>()
            .ServeAsync(actorId, TestContext.Current.CancellationToken);
        if (served is null) return null;
        await served.Bytes.DisposeAsync();
        return served.MediaType;
    }

    private static async Task<long> HoldAsync(TestDatabase database, Guid prdbId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var video = new CatalogueVideoRow { PrdbId = prdbId, Title = prdbId.ToString("D") };

        context.CatalogueVideos.Add(video);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return video.Id;
    }

    private static async Task GiveArtworkAsync(
        TestDatabase database,
        long videoId,
        Guid imageId,
        string url,
        int position = 0)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.CatalogueImages.Add(new CatalogueImageRow
        {
            PrdbId = imageId,
            VideoId = videoId,
            Url = url,
            Position = position,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WantAsync(TestDatabase database, long videoId, DateTimeOffset since)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.WantedVideos.Add(new WantedVideoRow { VideoId = videoId, SinceAt = since });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> IsCachedAsync(TestDatabase database, Guid imageId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        return await context.CatalogueImages
            .Where(row => row.PrdbId == imageId)
            .Select(row => row.Cached)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> IsDeadAsync(TestDatabase database, Guid imageId)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        return await context.CatalogueImages
            .Where(row => row.PrdbId == imageId)
            .Select(row => row.FoundDead)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
