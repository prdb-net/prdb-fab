using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests.Acquisition;

public sealed class DownloadRouteTests
{
    [Fact]
    public async Task Downloads_are_local_newest_first_and_filterable_by_state_and_indexer()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        using var response = await client.GetAsync(
            $"/api/downloads?state=Failed&indexer={seeded.IndexerId}",
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var page = (await response.Content.ReadFromJsonAsync<Page>(TestContext.Current.CancellationToken))!;

        var row = Assert.Single(page.Downloads);
        Assert.Equal("Second Release", row.SubmittedName);
        Assert.Equal("A Video", row.VideoTitle);
        Assert.Equal("Failed", row.State);
        Assert.Equal("Vanished", row.Cause);
        Assert.Equal("Fixture", row.Indexer.Name);
    }

    [Fact]
    public async Task Stop_following_previews_the_exact_outstanding_selection_and_never_writes_sabnzbd()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        using var preview = await client.PostAsJsonAsync(
            "/api/downloads/stop-following/preview",
            new { downloadIds = new[] { seeded.OutstandingId } },
            TestContext.Current.CancellationToken);
        preview.EnsureSuccessStatusCode();
        Assert.Equal("Ready", (await preview.Content.ReadFromJsonAsync<Selection>(TestContext.Current.CancellationToken))!.Outcome);

        using var action = await client.PostAsJsonAsync(
            "/api/downloads/stop-following",
            new { downloadIds = new[] { seeded.OutstandingId } },
            TestContext.Current.CancellationToken);
        action.EnsureSuccessStatusCode();
        Assert.Equal("Stopped", (await action.Content.ReadFromJsonAsync<Selection>(TestContext.Current.CancellationToken))!.Outcome);

        await using var scope = application.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
            .SingleAsync(download => download.Id == seeded.OutstandingId, TestContext.Current.CancellationToken);
        Assert.Equal(DownloadState.Failed, row.State);
        Assert.Equal(DownloadCause.Abandoned, row.Cause);
    }

    [Fact]
    public async Task Reset_requires_the_previewed_video_history_and_deletes_only_that_local_history()
    {
        await using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();
        var seeded = await SeedAsync(application);

        using var previewResponse = await client.PostAsync(
            $"/api/releases/video/{seeded.VideoId}/reset-downloads/preview",
            null,
            TestContext.Current.CancellationToken);
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<ResetPreview>(TestContext.Current.CancellationToken))!;
        Assert.Equal("Ready", preview.Outcome);
        Assert.Equal(2, preview.Downloads.Count);

        using var stale = await client.PostAsJsonAsync(
            $"/api/releases/video/{seeded.VideoId}/reset-downloads",
            new { downloadIds = new[] { seeded.OutstandingId } },
            TestContext.Current.CancellationToken);
        Assert.Equal("SelectionChanged", (await stale.Content.ReadFromJsonAsync<Selection>(TestContext.Current.CancellationToken))!.Outcome);

        using var action = await client.PostAsJsonAsync(
            $"/api/releases/video/{seeded.VideoId}/reset-downloads",
            new { downloadIds = preview.Downloads.Select(row => row.Id).ToArray() },
            TestContext.Current.CancellationToken);
        action.EnsureSuccessStatusCode();
        Assert.Equal("Reset", (await action.Content.ReadFromJsonAsync<Selection>(TestContext.Current.CancellationToken))!.Outcome);

        await using var scope = application.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
            .CountAsync(download => download.VideoId == seeded.VideoId, TestContext.Current.CancellationToken));
    }

    private static async Task<Seeded> SeedAsync(FabApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var now = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
        var videoId = Guid.NewGuid();
        var indexerId = Guid.NewGuid();
        context.Indexers.Add(new IndexerRow
        {
            Id = indexerId,
            Name = "Fixture",
            Url = "https://indexer.invalid/api",
            ApiKey = "fixture",
            LastVerdict = IndexerConnectionOutcome.Saved,
        });
        context.CatalogueVideos.Add(new CatalogueVideoRow
        {
            PrdbId = videoId,
            Title = "A Video",
            NormalisedTitle = "a video",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        var outstanding = Download(videoId, indexerId, "First Release", now, DownloadState.Outstanding, null);
        var failed = Download(videoId, indexerId, "Second Release", now.AddMinutes(1), DownloadState.Failed, DownloadCause.Vanished);
        context.Downloads.AddRange(outstanding, failed);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(videoId, indexerId, outstanding.Id);
    }

    private static DownloadRow Download(
        Guid videoId,
        Guid indexerId,
        string name,
        DateTimeOffset at,
        DownloadState state,
        DownloadCause? cause) => new()
        {
            Id = Guid.CreateVersion7(at),
            VideoId = videoId,
            IndexerId = indexerId,
            DerivedReleaseId = name,
            SubmittedName = name,
            NzoId = "nzo-" + name,
            State = state,
            Cause = cause,
            OutstandingSince = at,
            OriginIsPerson = true,
            CreatedAt = at,
        };

    private sealed record Seeded(Guid VideoId, Guid IndexerId, Guid OutstandingId);
    private sealed record Indexer(Guid Id, string Name);
    private sealed record Row(Guid Id, string SubmittedName, string VideoTitle, string State, string? Cause, Indexer Indexer);
    private sealed record Page(IReadOnlyList<Row> Downloads);
    private sealed record Selection(string Outcome);
    private sealed record ResetRow(Guid Id);
    private sealed record ResetPreview(string Outcome, IReadOnlyList<ResetRow> Downloads);
}
