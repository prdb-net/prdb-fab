using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.ReleaseDiscovery;

/// <summary>The shared Release table, driven entirely from the local Indexer Cache.</summary>
public sealed class ReleaseRouteTests
{
    [Fact]
    public async Task One_table_serves_video_site_and_actor_contexts_without_remote_work()
    {
        var prdb = new RefusesEverything();
        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        var video = await ReadAsync(client, $"video={seeded.VideoId}");
        var site = await ReadAsync(client, $"site={seeded.SiteId}");
        var actor = await ReadAsync(client, $"actor={seeded.ActorId}");

        Assert.Equal("Video", video.Context.Kind);
        Assert.Equal(["Ambiguous.Release", "Matched.Release"], video.Releases.Select(row => row.Title).Order());
        Assert.Equal("Site", site.Context.Kind);
        Assert.Equal(3, site.Total);
        Assert.Equal("Actor", actor.Context.Kind);
        Assert.Equal(2, actor.Total);
        Assert.Equal(0, prdb.Requests);
    }

    [Fact]
    public async Task Candidates_and_a_site_only_match_are_never_presented_as_a_video()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        var page = await ReadAsync(client, $"site={seeded.SiteId}");
        var matched = page.Releases.Single(row => row.IdentificationState == "Matched");
        var ambiguous = page.Releases.Single(row => row.IdentificationState == "Ambiguous");
        var siteOnly = page.Releases.Single(row => row.IdentificationState == "SiteOnly");

        Assert.NotNull(matched.Video);
        Assert.Empty(matched.Candidates);
        Assert.Null(matched.SiteOnlyMatch);

        Assert.Null(ambiguous.Video);
        Assert.Equal(2, ambiguous.Candidates.Count);
        Assert.Null(ambiguous.SiteOnlyMatch);

        Assert.Null(siteOnly.Video);
        Assert.Empty(siteOnly.Candidates);
        Assert.Equal("Northline", siteOnly.SiteOnlyMatch?.Title);
    }

    [Fact]
    public async Task State_and_indexer_filters_live_in_the_request_address()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        var page = await ReadAsync(
            client,
            $"site={seeded.SiteId}&state=Ambiguous&indexer={seeded.IndexerId}&page=1");

        Assert.Equal("Ambiguous.Release", Assert.Single(page.Releases).Title);
        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Exactly_one_context_is_required_and_the_table_is_authenticated()
    {
        await using var application = new FabApplication();
        using var signedIn = await application.SignedInClientAsync();
        using var anonymous = application.CreateClient();

        using var absent = await signedIn.GetAsync("/api/releases", TestContext.Current.CancellationToken);
        using var competing = await signedIn.GetAsync(
            $"/api/releases?video={Guid.NewGuid()}&site={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);
        using var protectedAnswer = await anonymous.GetAsync(
            $"/api/releases?video={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, competing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedAnswer.StatusCode);
    }

    private static async Task<Answer> ReadAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"/api/releases?{query}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<Seeded> SeedAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var site = new CatalogueSiteRow { PrdbId = Guid.NewGuid(), Title = "Northline" };
        var actor = new CatalogueActorRow { PrdbId = Guid.NewGuid(), Name = "Mira Vance" };
        var indexer = new IndexerRow
        {
            Id = Guid.NewGuid(),
            Name = "Local Indexer",
            Url = "https://indexer.invalid/api",
            ApiKey = "fixture",
            LastVerdict = IndexerConnectionOutcome.Saved,
        };
        context.AddRange(site, actor, indexer);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var video = Video("First Light", site.Id, 1);
        var other = Video("Second Shift", site.Id, 2);
        context.AddRange(video, other);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.CatalogueVideoActors.Add(new CatalogueVideoActorRow { VideoId = video.Id, ActorId = actor.Id });

        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var matched = Release("Matched.Release", indexer.Id, now.AddMinutes(-3), IdentificationState.Matched);
        matched.VideoId = video.Id;
        matched.Confidence = IdentificationConfidence.Exact;
        matched.MatchedBy = IdentificationRung.ReleaseName;
        var ambiguous = Release("Ambiguous.Release", indexer.Id, now.AddMinutes(-2), IdentificationState.Ambiguous);
        var siteOnly = Release("SiteOnly.Release", indexer.Id, now.AddMinutes(-1), IdentificationState.SiteOnly);
        siteOnly.SiteId = site.Id;
        var unknown = Release("Unknown.Release", indexer.Id, now, IdentificationState.Unknown);
        context.AddRange(matched, ambiguous, siteOnly, unknown);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ReleaseCandidates.AddRange(
            new ReleaseCandidateRow { ReleaseId = ambiguous.Id, VideoId = video.Id },
            new ReleaseCandidateRow { ReleaseId = ambiguous.Id, VideoId = other.Id });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new(video.PrdbId, site.PrdbId, actor.PrdbId, indexer.Id);
    }

    private static CatalogueVideoRow Video(string title, long siteId, int day) => new()
    {
        PrdbId = Guid.NewGuid(),
        Title = title,
        NormalisedTitle = title.ToLowerInvariant(),
        SiteId = siteId,
        CreatedAtUtc = new DateTimeOffset(2026, 8, day, 12, 0, 0, TimeSpan.Zero),
        UpdatedAtUtc = new DateTimeOffset(2026, 8, day, 12, 0, 0, TimeSpan.Zero),
    };

    private static ReleaseRow Release(
        string title,
        Guid indexerId,
        DateTimeOffset firstSeen,
        IdentificationState state) => new()
        {
            IndexerId = indexerId,
            DerivedReleaseId = title,
            RawGuid = title,
            Title = title,
            NormalisedTitle = title.ToLowerInvariant(),
            DownloadUrl = "https://indexer.invalid/download",
            FirstSeenAt = firstSeen,
            PostDate = firstSeen,
            PubDate = firstSeen,
            Size = 2_000_000_000,
            IdentificationState = state,
        };

    private sealed record Seeded(Guid VideoId, Guid SiteId, Guid ActorId, Guid IndexerId);
    private sealed record Context(string Kind, Guid PrdbId, string Title);
    private sealed record IdentifiedVideo(Guid PrdbId, string Title);
    private sealed record Candidate(Guid PrdbId, string Title);
    private sealed record SiteOnly(Guid PrdbId, string Title);
    private sealed record Row(
        string Title,
        string IdentificationState,
        IdentifiedVideo? Video,
        IReadOnlyList<Candidate> Candidates,
        SiteOnly? SiteOnlyMatch);
    private sealed record Answer(Context Context, IReadOnlyList<Row> Releases, int Page, int Total);

    private sealed class RefusesEverything : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            throw new HttpRequestException("A Release read must not reach prdb.");
        }
    }
}
