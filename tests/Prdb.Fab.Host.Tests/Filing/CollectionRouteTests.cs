using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Host.Tests.Filing;

public sealed class CollectionRouteTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Collection_routes_require_a_session()
    {
        await using var app = new FabApplication();
        _ = await app.SignedInClientAsync();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/collection", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/collection/approve", Approval(1), Token)).StatusCode);
    }

    [Fact]
    public async Task Partial_decisions_stale_replays_and_explicit_unresolved_dismissal_do_not_move_files()
    {
        await using var app = new FabApplication();
        using var client = await app.SignedInClientAsync();
        var (ready, unresolved) = await SeedAsync(app);
        var preview = await client.GetFromJsonAsync<CollectionPreviewPage>("/api/collection", Json, Token);
        Assert.Equal(1, preview!.Ready);
        Assert.Equal(1, preview.Unresolved);
        Assert.False((await ApproveAsync(client, Approval(preview.Revision - 1))).Accepted);
        var selection = Approval(preview.Revision, [ready]);
        Assert.True((await ApproveAsync(client, selection)).Accepted);
        Assert.False((await ApproveAsync(client, selection)).Accepted);
        preview = await client.GetFromJsonAsync<CollectionPreviewPage>("/api/collection", Json, Token);
        Assert.Equal("Preview", preview!.Phase);
        Assert.Equal(1, preview.Approved);
        Assert.True((await ApproveAsync(client, Approval(preview.Revision, leave: [unresolved]))).Accepted);
        preview = await client.GetFromJsonAsync<CollectionPreviewPage>("/api/collection", Json, Token);
        Assert.Equal("Validating", preview!.Phase);
        Assert.All(preview.Files, file => Assert.True(File.Exists(file.SourcePath)));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Empty(await db.ArrivingFileCandidates.ToListAsync(Token));
        Assert.Single(await db.ArrivingFiles.ToListAsync(Token));
        Assert.Empty(await db.Downloads.ToListAsync(Token));
    }

    [Fact]
    public async Task Changed_identification_invalidates_the_whole_preview_without_approval()
    {
        await using var app = new FabApplication();
        using var client = await app.SignedInClientAsync();
        var (ready, _) = await SeedAsync(app);
        var preview = await client.GetFromJsonAsync<CollectionPreviewPage>("/api/collection", Json, Token);
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles.Where(row => row.Id == ready)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.QualityLabel, "720p"), Token);
        Assert.False((await ApproveAsync(client, Approval(preview!.Revision, [ready]))).Accepted);
        preview = await client.GetFromJsonAsync<CollectionPreviewPage>("/api/collection", Json, Token);
        Assert.Equal("Planning", preview!.Phase);
        Assert.Equal(0, preview.Approved);
    }

    private static object Approval(long revision, Guid[]? move = null, Guid[]? leave = null) =>
        new { revision, moveIds = move ?? [], leaveIds = leave ?? [], allReady = false, leaveUnresolved = false };

    private static async Task<CollectionAct> ApproveAsync(HttpClient client, object request)
    {
        using var response = await client.PostAsJsonAsync("/api/collection/approve", request, Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CollectionAct>(Token))!;
    }

    private static async Task<(Guid Ready, Guid Unresolved)> SeedAsync(FabApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var library = Path.Combine(app.DataDirectory, "library");
        Directory.CreateDirectory(library);
        await db.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.LibraryRoot, library), Token);
        var site = Guid.NewGuid();
        var video = Guid.NewGuid();
        var siteRow = new CatalogueSiteRow { PrdbId = site, Title = "Site" };
        db.CatalogueSites.Add(siteRow);
        db.CatalogueVideos.Add(new() { PrdbId = video, Site = siteRow, Title = "Title" });
        db.CollectionPreparations.Add(new() { Phase = "Planning", StartedAt = DateTimeOffset.UtcNow });
        var root = Guid.NewGuid();
        db.CollectionRoots.Add(new() { Id = root, Path = app.DataDirectory });
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
        {
            var path = Path.Combine(app.DataDirectory, id + ".mkv");
            await File.WriteAllTextAsync(path, "fixture", Token);
            db.CollectionFiles.Add(new() { Id = id, RootId = root, SourcePath = path, SizeBytes = 7, Probed = true, LastWriteAt = File.GetLastWriteTimeUtc(path) });
            db.ArrivingFiles.Add(new() { Id = id, SourcePath = path, ArrivedName = id + ".mkv", SizeBytes = 7,
                VideoId = id == ids[0] ? video : null, QualityLabel = "1080p", Confidence = IdentificationConfidence.Exact,
                State = id == ids[0] ? ArrivingFileState.AwaitingFiling : ArrivingFileState.AwaitingIdentification,
                Reason = id == ids[0] ? null : ArrivingFileReason.Unidentified });
        }
        db.ArrivingFileCandidates.Add(new() { ArrivingFileId = ids[1], VideoId = video });
        await db.SaveChangesAsync(Token);
        await scope.ServiceProvider.GetRequiredService<CollectionPlanningRoutine>().RunAsync(null, Token);
        return (ids[0], ids[1]);
    }
}
