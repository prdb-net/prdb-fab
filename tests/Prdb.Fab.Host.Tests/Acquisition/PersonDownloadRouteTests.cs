using System.Net;
using System.Net.Http.Json;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Host.Tests.Connections;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Acquisition;

public sealed class PersonDownloadRouteTests
{
    [Fact]
    public async Task A_previewed_person_download_is_reserved_submitted_and_idempotent()
    {
        var indexer = new NzbIndexer();
        var sabnzbd = new FakeSabnzbd();
        await using var application = Application(indexer, sabnzbd);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        var preview = await PreviewAsync(client, seeded);
        Assert.Equal("Ready", preview.Outcome);
        Assert.NotNull(preview.DownloadId);
        Assert.Equal(seeded.ReleaseId, preview.Release.Id);

        var request = new { downloadId = preview.DownloadId!.Value, videoId = seeded.VideoId };
        var first = await PostAsync<Verdict>(client, $"/api/releases/{seeded.ReleaseId}/download", request);
        var repeated = await PostAsync<Verdict>(client, $"/api/releases/{seeded.ReleaseId}/download", request);

        Assert.True(first.Outcome == "Submitted", $"{first.Outcome}: {first.Detail}; modes: {string.Join(',', sabnzbd.Modes)}");
        Assert.Equal(first, repeated);
        Assert.Equal("SABnzbd_nzo_fixture", first.NzoId);
        Assert.Equal(1, indexer.Requests);
        Assert.Equal(1, sabnzbd.Modes.Count(mode => mode == "get_cats"));
        Assert.Equal(1, sabnzbd.Modes.Count(mode => mode == "addfile"));
        Assert.DoesNotContain("retry", sabnzbd.Modes);
        Assert.DoesNotContain("delete", sabnzbd.Modes);
        Assert.Equal("xxx", sabnzbd.LastAddFileCategory);
        Assert.Equal("A.Release.Name", sabnzbd.LastAddFileName);
        Assert.Contains("name=name", sabnzbd.LastAddFileBody);
        Assert.Contains("filename=release.nzb", sabnzbd.LastAddFileBody);
        Assert.Contains(NzbIndexer.Nzb, sabnzbd.LastAddFileBody);

        await using var scope = application.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(7, stored.Id.Version);
        Assert.True(stored.OriginIsPerson);
        Assert.Equal(DownloadState.Outstanding, stored.State);
        Assert.Equal("A.Release.Name", stored.SubmittedName);
        Assert.Equal("SABnzbd_nzo_fixture", stored.NzoId);
        Assert.Equal(seeded.VideoId, stored.VideoId);

        var log = string.Join(
            '\n',
            Directory.GetFiles(Path.Combine(application.DataDirectory, "logs"), "*.log")
                .Select(File.ReadAllText));
        Assert.DoesNotContain(FakeSabnzbd.RightKey, log, StringComparison.Ordinal);
        Assert.DoesNotContain(NzbIndexer.Secret, log, StringComparison.Ordinal);
        Assert.DoesNotContain("/nzb?", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_nzo_id_is_a_readable_failed_download_and_still_consumes_the_release()
    {
        var sabnzbd = new FakeSabnzbd { AddFileNzoIds = [] };
        await using var application = Application(new NzbIndexer(), sabnzbd);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);
        var preview = await PreviewAsync(client, seeded);

        var verdict = await PostAsync<Verdict>(
            client,
            $"/api/releases/{seeded.ReleaseId}/download",
            new { downloadId = preview.DownloadId!.Value, videoId = seeded.VideoId });

        Assert.True(verdict.Outcome == "Rejected", $"{verdict.Outcome}: {verdict.Detail}; modes: {string.Join(',', sabnzbd.Modes)}");
        Assert.Equal("Failed", verdict.State);
        Assert.Equal("Rejected", verdict.Cause);

        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var stored = await context.Downloads.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DownloadState.Failed, stored.State);
        Assert.Equal(DownloadCause.Rejected, stored.Cause);

        var secondPreview = await PreviewAsync(client, seeded);
        Assert.Equal("NoReleasesLeft", secondPreview.Outcome);
    }

    [Fact]
    public async Task An_unknown_checked_category_blocks_before_nzb_fetch_and_creates_nothing()
    {
        var indexer = new NzbIndexer();
        var sabnzbd = new FakeSabnzbd();
        await using var application = Application(indexer, sabnzbd);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application, category: "gone");
        var preview = await PreviewAsync(client, seeded);

        var verdict = await PostAsync<Verdict>(
            client,
            $"/api/releases/{seeded.ReleaseId}/download",
            new { downloadId = preview.DownloadId!.Value, videoId = seeded.VideoId });

        Assert.Equal("ConnectionProblem", verdict.Outcome);
        Assert.Equal(0, indexer.Requests);
        Assert.DoesNotContain("addfile", sabnzbd.Modes);

        await using var scope = application.Services.CreateAsyncScope();
        Assert.Equal(
            0,
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads.CountAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unanswered_addfile_is_reserved_and_never_repeated_blindly()
    {
        var sabnzbd = new FakeSabnzbd
        {
            AddFileThrows = new HttpRequestException("The answer was lost."),
        };
        await using var application = Application(new NzbIndexer(), sabnzbd);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);
        var preview = await PreviewAsync(client, seeded);
        var request = new { downloadId = preview.DownloadId!.Value, videoId = seeded.VideoId };

        var first = await PostAsync<Verdict>(client, $"/api/releases/{seeded.ReleaseId}/download", request);
        var repeated = await PostAsync<Verdict>(client, $"/api/releases/{seeded.ReleaseId}/download", request);

        Assert.Equal("SubmissionUnknown", first.Outcome);
        Assert.Equal(first, repeated);
        Assert.Equal(1, sabnzbd.Modes.Count(mode => mode == "addfile"));

        await using var scope = application.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(DownloadState.Outstanding, stored.State);
        Assert.Null(stored.NzoId);
    }

    [Fact]
    public async Task An_indexer_redirect_is_not_followed_and_nothing_is_submitted()
    {
        var indexer = new NzbIndexer { Status = HttpStatusCode.Redirect };
        var sabnzbd = new FakeSabnzbd();
        await using var application = Application(indexer, sabnzbd);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);
        var preview = await PreviewAsync(client, seeded);

        var verdict = await PostAsync<Verdict>(
            client,
            $"/api/releases/{seeded.ReleaseId}/download",
            new { downloadId = preview.DownloadId!.Value, videoId = seeded.VideoId });

        Assert.Equal("IndexerProblem", verdict.Outcome);
        Assert.Equal(1, indexer.Requests);
        Assert.DoesNotContain("addfile", sabnzbd.Modes);
    }

    [Fact]
    public async Task An_http_nzb_from_the_configured_https_indexer_is_upgraded_without_following_a_redirect()
    {
        var indexer = new NzbIndexer();
        var sabnzbd = new FakeSabnzbd();
        await using var application = Application(indexer, sabnzbd);
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application, downloadUrl: $"http://indexer.invalid/nzb?apikey={NzbIndexer.Secret}");
        var preview = await PreviewAsync(client, seeded);

        var verdict = await PostAsync<Verdict>(
            client,
            $"/api/releases/{seeded.ReleaseId}/download",
            new { downloadId = preview.DownloadId!.Value, videoId = seeded.VideoId });

        Assert.Equal("Submitted", verdict.Outcome);
        Assert.Equal(Uri.UriSchemeHttps, indexer.LastRequest?.Scheme);
        Assert.Equal(1, indexer.Requests);
        Assert.Equal(1, sabnzbd.Modes.Count(mode => mode == "addfile"));
    }

    [Fact]
    public async Task The_action_is_authenticated_and_never_returns_remote_credentials_or_the_download_url()
    {
        await using var application = Application(new NzbIndexer(), new FakeSabnzbd());
        using var signedIn = await application.SignedInClientAsync();
        using var anonymous = application.CreateClient();
        var seeded = await SeedAsync(application);

        using var protectedResponse = await anonymous.PostAsJsonAsync(
            $"/api/releases/{seeded.ReleaseId}/download/preview",
            new { videoId = seeded.VideoId },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);

        using var invented = await signedIn.PostAsJsonAsync(
            $"/api/releases/{seeded.ReleaseId}/download",
            new { downloadId = Guid.NewGuid(), videoId = seeded.VideoId },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invented.StatusCode);

        using var preview = await signedIn.PostAsJsonAsync(
            $"/api/releases/{seeded.ReleaseId}/download/preview",
            new { videoId = seeded.VideoId },
            TestContext.Current.CancellationToken);
        var body = await preview.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FakeSabnzbd.RightKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(NzbIndexer.Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("/nzb", body, StringComparison.Ordinal);
    }

    private static FabApplication Application(HttpMessageHandler indexer, HttpMessageHandler sabnzbd) =>
        new FabApplication()
            .Answering(FabTransports.Indexers, indexer)
            .Answering(FabTransports.Sabnzbd, sabnzbd);

    private static async Task<Seeded> SeedAsync(
        FabApplication application,
        string category = "xxx",
        string? downloadUrl = null)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);
        installation.SabnzbdUrl = "http://sabnzbd.invalid:8080";
        installation.SabnzbdApiKey = FakeSabnzbd.RightKey;
        installation.SabnzbdCategory = category;
        context.Installation.Update(installation);

        var indexer = new IndexerRow
        {
            Id = Guid.Parse("0198ec28-1c00-7000-8000-000000000031"),
            Name = "Fixture indexer",
            Url = "https://indexer.invalid/api",
            ApiKey = NzbIndexer.Secret,
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
            Rank = 1,
        };
        var video = new CatalogueVideoRow
        {
            PrdbId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000031"),
            Title = "A Video",
            NormalisedTitle = "a video",
            CreatedAtUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
        };
        context.AddRange(indexer, video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var release = new ReleaseRow
        {
            IndexerId = indexer.Id,
            DerivedReleaseId = "outside-id-31",
            RawGuid = "raw-31",
            Title = "A.Release.Name",
            NormalisedTitle = "a release name",
            Size = 2_000_000_000,
            Categories = "[]",
            PostDate = new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.Zero),
            PubDate = new DateTimeOffset(2026, 8, 28, 11, 0, 0, TimeSpan.Zero),
            DownloadUrl = downloadUrl ?? $"https://indexer.invalid/nzb?apikey={NzbIndexer.Secret}",
            FirstSeenAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
            IdentificationState = IdentificationState.Matched,
            VideoId = video.Id,
            Confidence = IdentificationConfidence.Exact,
            MatchedBy = IdentificationRung.ReleaseName,
        };
        context.Releases.Add(release);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(video.PrdbId, release.Id);
    }

    private static Task<Preview> PreviewAsync(HttpClient client, Seeded seeded) => PostAsync<Preview>(
        client,
        $"/api/releases/{seeded.ReleaseId}/download/preview",
        new { videoId = seeded.VideoId });

    private static async Task<T> PostAsync<T>(HttpClient client, string address, object request)
    {
        using var response = await client.PostAsJsonAsync(address, request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private sealed record Seeded(Guid VideoId, long ReleaseId);
    private sealed record Choice(long Id);
    private sealed record Preview(string Outcome, Guid? DownloadId, Choice Release);
    private sealed record Verdict(string Outcome, Guid DownloadId, string? State, string? Cause, string? NzoId, string Detail);

    private sealed class NzbIndexer : HttpMessageHandler
    {
        public const string Secret = "fixture-indexer-key-not-real";
        public const string Nzb = "<?xml version=\"1.0\"?><nzb></nzb>";

        public int Requests { get; private set; }

        public Uri? LastRequest { get; private set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastRequest = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Nzb, Encoding.UTF8, "application/x-nzb"),
            });
        }
    }
}
