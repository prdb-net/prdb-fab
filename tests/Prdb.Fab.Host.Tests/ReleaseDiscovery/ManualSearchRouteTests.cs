using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Host.Tests.Connections;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

using Xunit;

namespace Prdb.Fab.Host.Tests.ReleaseDiscovery;

public sealed class ManualSearchRouteTests
{
    [Fact]
    public async Task The_manual_path_runs_from_local_video_search_to_a_submitted_release()
    {
        var videoId = Guid.NewGuid();
        var indexer = new WorkflowIndexer();
        var prdb = new IdentificationPrdb(videoId);
        var sabnzbd = new FakeSabnzbd();
        await using var application = new FabApplication()
            .Answering(FabTransports.Indexers, indexer)
            .Answering(FabTransports.Prdb, prdb)
            .Answering(FabTransports.Sabnzbd, sabnzbd);
        using var client = await application.SignedInClientAsync();
        await SeedAsync(application, videoId, configureRemotePath: true);

        var local = await client.GetFromJsonAsync<VideoPage>(
            "/api/catalogue/videos?search=Manual%20Scene",
            TestContext.Current.CancellationToken);
        Assert.Equal(videoId, Assert.Single(local!.Videos).PrdbId);

        var started = await PostAsync<StartVerdict>(client, "/api/releases/searches", new
        {
            videoId,
            indexerId = (Guid?)null,
        });
        Assert.Equal("Started", started.Outcome);

        await using (var turn = application.Services.CreateAsyncScope())
        {
            await turn.ServiceProvider.GetRequiredService<ManualSearchRoutine>()
                .RunAsync(started.SearchId!.Value.ToString("D"), TestContext.Current.CancellationToken);
        }
        await using (var identify = application.Services.CreateAsyncScope())
        {
            var result = await identify.ServiceProvider.GetRequiredService<ReleaseIdentificationRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ItemsHandled);
        }

        var releases = await client.GetFromJsonAsync<ReleasePage>(
            $"/api/releases?video={videoId}&page=1",
            TestContext.Current.CancellationToken);
        var release = Assert.Single(releases!.Releases);
        Assert.Equal("Matched", release.IdentificationState);
        Assert.NotNull(release.RankingPosition);

        var preview = await PostAsync<Preview>(
            client,
            $"/api/releases/{release.Id}/download/preview",
            new { videoId });
        Assert.Equal("Ready", preview.Outcome);
        var download = await PostAsync<DownloadVerdict>(
            client,
            $"/api/releases/{release.Id}/download",
            new { videoId, downloadId = preview.DownloadId });

        Assert.Equal("Submitted", download.Outcome);
        Assert.Contains("search", indexer.Functions);
        Assert.Equal(1, indexer.NzbRequests);
        Assert.Equal(1, sabnzbd.Modes.Count(mode => mode == "addfile"));
        Assert.Contains("/videos/identify", prdb.Paths);
    }

    [Fact]
    public async Task A_signed_in_person_can_queue_and_read_a_video_scoped_search()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        var videoId = await SeedAsync(application);

        using var started = await client.PostAsJsonAsync(
            "/api/releases/searches",
            new { videoId, indexerId = (Guid?)null },
            TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();
        var verdict = await started.Content.ReadFromJsonAsync<StartVerdict>(TestContext.Current.CancellationToken);
        Assert.NotNull(verdict);
        Assert.Contains(verdict.Outcome, new[] { "Started", "AlreadyRunning" });
        Assert.NotNull(verdict.SearchId);

        var latest = await client.GetFromJsonAsync<SearchView>(
            $"/api/releases/searches/latest?video={videoId}",
            TestContext.Current.CancellationToken);
        Assert.NotNull(latest);
        Assert.Equal(videoId, latest.VideoId);
        Assert.Equal("Manual Scene", latest.VideoTitle);

        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.True(await context.ManualSearches.AnyAsync(row =>
            row.Id == verdict.SearchId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Manual_search_routes_are_behind_the_password()
    {
        await using var application = new FabApplication();
        using var client = application.CreateClient();

        using var start = await client.PostAsJsonAsync(
            "/api/releases/searches",
            new { videoId = Guid.NewGuid(), indexerId = (Guid?)null },
            TestContext.Current.CancellationToken);
        using var latest = await client.GetAsync(
            $"/api/releases/searches/latest?video={Guid.NewGuid()}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, start.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, latest.StatusCode);
    }

    private static async Task<Guid> SeedAsync(
        FabApplication application,
        Guid? suppliedVideoId = null,
        bool configureRemotePath = false)
    {
        var videoId = suppliedVideoId ?? Guid.NewGuid();
        var indexerId = Guid.NewGuid();
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        if (configureRemotePath)
        {
            var installation = await context.Installation.AsTracking().SingleAsync(TestContext.Current.CancellationToken);
            installation.PrdbApiKey = "prdb-key";
            installation.SabnzbdUrl = "http://sabnzbd.invalid:8080";
            installation.SabnzbdApiKey = FakeSabnzbd.RightKey;
            installation.SabnzbdCategory = "xxx";
        }
        context.CatalogueVideos.Add(new CatalogueVideoRow
        {
            PrdbId = videoId,
            Title = "Manual Scene",
            NormalisedTitle = "manual scene",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        context.Indexers.Add(new IndexerRow
        {
            Id = indexerId,
            Name = "Manual Indexer",
            Url = "https://indexer.invalid/api",
            ApiKey = "key",
            Categories = "Adult",
            Enabled = true,
            DailyQueryBudget = 100,
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = DateTimeOffset.UtcNow,
        });
        context.IndexerWalkStates.Add(new IndexerWalkStateRow
        {
            IndexerId = indexerId,
            CapsTree = "[]",
            ResolvedCategoryIds = "[]",
            MissingCategoryNames = "[]",
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return videoId;
    }

    private sealed record StartVerdict(string Outcome, Guid? SearchId);
    private sealed record SearchView(Guid Id, Guid VideoId, string VideoTitle, string Phase);
    private sealed record VideoCard(Guid PrdbId);
    private sealed record VideoPage(IReadOnlyList<VideoCard> Videos);
    private sealed record ReleaseItem(long Id, string IdentificationState, int? RankingPosition);
    private sealed record ReleasePage(IReadOnlyList<ReleaseItem> Releases);
    private sealed record Preview(string Outcome, Guid? DownloadId);
    private sealed record DownloadVerdict(string Outcome);

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private sealed class WorkflowIndexer : HttpMessageHandler
    {
        public List<string> Functions { get; } = [];
        public int NzbRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var function = query["t"] ?? string.Empty;
            Functions.Add(function);
            if (request.RequestUri.AbsolutePath == "/nzb")
            {
                NzbRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<?xml version=\"1.0\"?><nzb></nzb>", Encoding.UTF8, "application/x-nzb"),
                });
            }

            const string search = """
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
                  <channel><item>
                    <title>Manual.Scene.1080p</title>
                    <guid>manual-result</guid>
                    <link>https://indexer.invalid/nzb?apikey=indexer-key</link>
                    <pubDate>Thu, 27 Aug 2026 08:00:00 +0000</pubDate>
                    <newznab:attr name="size" value="2000000000" />
                    <newznab:attr name="category" value="5010" />
                  </item></channel>
                </rss>
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(search, Encoding.UTF8, "application/xml"),
            });
        }
    }

    private sealed class IdentificationPrdb(Guid videoId) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            string body;
            if (path == "/videos/identify")
            {
                var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var reference = sent.RootElement.GetProperty("files")[0].GetProperty("ref").GetString();
                body = $$"""{"results":[{"ref":"{{reference}}","videoId":"{{videoId}}","confidence":4,"matchedBy":3,"candidates":[]}]}""";
            }
            else if (path == "/videos/batch")
            {
                body = $$"""[{"id":"{{videoId}}","title":"Manual Scene","preNames":[],"actors":[],"images":[]}]""";
            }
            else
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
