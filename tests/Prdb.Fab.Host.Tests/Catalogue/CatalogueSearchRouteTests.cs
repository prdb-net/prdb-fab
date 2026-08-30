using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>The top-level Search surface is a local, linkable acquisition view.</summary>
public sealed class CatalogueSearchRouteTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_default_is_available_to_acquire_with_newest_release_first()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application);

        var page = await ReadAsync(client, string.Empty);

        Assert.Equal(
            ["Newest available", "Ready scene", "Wanted scene", "Old available", "Undated available"],
            page.Videos.Select(video => video.Title));
    }

    [Theory]
    [InlineData("filter=DownloadReady", "Ready scene")]
    [InlineData("filter=NeedsSearch", "Newest available,Old available,Undated available,Wanted scene")]
    [InlineData("filter=Wanted", "Wanted scene")]
    [InlineData("filter=Held", "Held scene")]
    [InlineData("filter=Outstanding", "Outstanding scene")]
    public async Task Status_filters_select_the_population_before_paging(string query, string expected)
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application);

        var page = await ReadAsync(client, query + "&sort=TitleAscending");

        Assert.Equal(expected.Split(','), page.Videos.Select(video => video.Title));
    }

    [Fact]
    public async Task Sorts_include_prdb_recency_title_and_search_relevance()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application);

        var recentlyAdded = await ReadAsync(client, "filter=All&sort=CreatedDescending");
        var reverseTitle = await ReadAsync(client, "filter=All&sort=TitleDescending");
        var relevant = await ReadAsync(client, "filter=All&search=Ready%20scene&sort=Relevance");

        Assert.Equal("Undated available", recentlyAdded.Videos[0].Title);
        Assert.Equal("Wanted scene", reverseTitle.Videos[0].Title);
        Assert.Equal("Ready scene", relevant.Videos[0].Title);
    }

    private static async Task<Page> ReadAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<Page>(
            "/api/catalogue/videos" + (query.Length > 0 ? $"?{query}" : string.Empty),
            TestContext.Current.CancellationToken))!;

    private static async Task SeedAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var indexer = new IndexerRow
        {
            Id = Guid.NewGuid(),
            Name = "Fixture",
            Url = "https://indexer.invalid/api",
            ApiKey = "fixture",
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            Rank = 1,
        };
        context.Indexers.Add(indexer);

        var newest = Video("Newest available", new DateOnly(2026, 8, 29), 6);
        var ready = Video("Ready scene", new DateOnly(2026, 8, 20), 5);
        var wanted = Video("Wanted scene", new DateOnly(2026, 8, 18), 4);
        var old = Video("Old available", new DateOnly(2024, 1, 2), 3);
        var undated = Video("Undated available", null, 9);
        var held = Video("Held scene", new DateOnly(2026, 8, 28), 8);
        var outstanding = Video("Outstanding scene", new DateOnly(2026, 8, 27), 7);
        context.CatalogueVideos.AddRange(newest, ready, wanted, old, undated, held, outstanding);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.WantedVideos.Add(new WantedVideoRow { VideoId = wanted.Id, SinceAt = Noon });
        context.Releases.Add(new ReleaseRow
        {
            IndexerId = indexer.Id,
            DerivedReleaseId = "ready",
            RawGuid = "ready",
            Title = "Ready.Scene.1080p",
            NormalisedTitle = "ready scene 1080p",
            Categories = "[]",
            DownloadUrl = "https://indexer.invalid/ready.nzb",
            IdentificationState = IdentificationState.Matched,
            Confidence = IdentificationConfidence.Exact,
            VideoId = ready.Id,
        });
        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = held.PrdbId,
            EntryDirectory = "/library/held",
            FiledAt = Noon,
        });
        context.VideoFiles.Add(new VideoFileRow
        {
            Id = Guid.CreateVersion7(),
            LibraryEntryVideoId = held.PrdbId,
            FiledPath = "/library/held/video.mkv",
            QualityLabel = "1080p",
        });
        context.Downloads.Add(new DownloadRow
        {
            Id = Guid.CreateVersion7(),
            VideoId = outstanding.PrdbId,
            IndexerId = indexer.Id,
            DerivedReleaseId = "outstanding",
            SubmittedName = "Outstanding.Scene",
            State = DownloadState.Outstanding,
            OutstandingSince = Noon,
            CreatedAt = Noon,
            OriginIsPerson = true,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static CatalogueVideoRow Video(string title, DateOnly? released, int createdDay) => new()
    {
        PrdbId = Guid.NewGuid(),
        Title = title,
        NormalisedTitle = title.ToLowerInvariant(),
        ReleaseDate = released,
        CreatedAtUtc = new DateTimeOffset(2026, 8, createdDay, 12, 0, 0, TimeSpan.Zero),
        UpdatedAtUtc = Noon,
    };

    private sealed record Card(string Title);
    private sealed record Page(IReadOnlyList<Card> Videos);
}
