using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Status;
using Xunit;

namespace Prdb.Fab.Host.Tests.Filing;

public sealed class MaintenanceRouteTests
{
    [Fact]
    public async Task Rechecking_crosses_the_authenticated_contract_and_returns_background_progress()
    {
        await using var application = new FabApplication().WithTime(new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var client = await application.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var video = Guid.NewGuid();
            var file = Guid.NewGuid();
            context.LibraryEntries.Add(new LibraryEntryRow { VideoId = video, EntryDirectory = "/missing-library" });
            context.VideoFiles.Add(new VideoFileRow { Id = file, LibraryEntryVideoId = video, FiledPath = "/missing-library/file.mkv", QualityLabel = "1080p" });
            context.LibraryVerifications.Add(new LibraryVerificationRow { VideoFileId = file, Outcome = LibraryVerification.Missing });
            await context.SaveChangesAsync(token);
        }
        var before = await client.GetFromJsonAsync<LibraryVerificationProgress>("/api/library/verification", token);
        Assert.Equal(1, before!.Unconfirmed);
        using var response = await client.PostAsJsonAsync("/api/library/verification", new { onlyUnconfirmed = true }, token);
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<LibraryVerificationProgress>(token))!.Pending);
        using var anonymous = application.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/library/verification", token)).StatusCode);
    }

    [Fact]
    public async Task Notification_settings_do_not_echo_the_destination_secret()
    {
        await using var application = new FabApplication().WithTime(new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var client = await application.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        using var saved = await client.PostAsJsonAsync("/api/settings/notifications",
            new { enabled = true, webhookUrl = "https://receiver.example/private-token" }, token);
        Assert.True((await saved.Content.ReadFromJsonAsync<NotificationSettingsVerdict>(token))!.Accepted);
        var state = await client.GetStringAsync("/api/settings/notifications", token);
        Assert.DoesNotContain("private-token", state);
        Assert.Contains("\"configured\":true", state);
        using var invalid = await client.PostAsJsonAsync("/api/settings/notifications",
            new { enabled = true, webhookUrl = "file:///private" }, token);
        Assert.False((await invalid.Content.ReadFromJsonAsync<NotificationSettingsVerdict>(token))!.Accepted);
    }

    [Fact]
    public async Task An_empty_return_preview_is_readable_and_cannot_approve_unspecified_moves()
    {
        await using var application = new FabApplication().WithTime(new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var client = await application.SignedInClientAsync();
        var token = TestContext.Current.CancellationToken;
        var preview = await client.GetFromJsonAsync<CollectionUndoPreview>("/api/collection/undo", token);
        Assert.NotNull(preview);
        Assert.Equal(0, preview.Available);
        using var response = await client.PostAsJsonAsync("/api/collection/undo",
            new { revision = preview.Revision, evidence = preview.Evidence, ids = Array.Empty<Guid>(), all = true }, token);
        Assert.False((await response.Content.ReadFromJsonAsync<CollectionAct>(token))!.Accepted);
    }
}
