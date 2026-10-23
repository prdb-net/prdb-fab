using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class CollectionImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "collection-import-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public CollectionImportTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_external_or_unrecorded_library_file_moves_and_only_then_opens_the_barrier(bool inLibrary)
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = await SeedAsync(db, inLibrary);
        await ApproveAsync(db);
        await RunImportAsync(db);
        Assert.Equal("Importing", await PhaseAsync(db));
        await RunFilingAsync(db);
        Assert.Equal("Importing", await PhaseAsync(db));
        await RunImportAsync(db);
        Assert.Equal("Ready", await PhaseAsync(db));
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var arrival = await context.ArrivingFiles.SingleAsync(Token);
        Assert.Equal(ArrivingFileState.Filed, arrival.State);
        Assert.False(File.Exists(arrival.SourcePath));
        var file = await context.VideoFiles.SingleAsync(Token);
        Assert.True(File.Exists(file.FiledPath));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file.FiledPath)!, EntryPath.SidecarFileName)));
        Assert.Empty(await context.Downloads.ToListAsync(Token));
        var log = await context.OperationLogEntries.SingleAsync(row => row.Act == "Filed", Token);
        Assert.Null(log.DownloadId);
        Assert.Equal(arrival.SourcePath, log.PathBefore);
        Assert.Equal(file.FiledPath, log.PathAfter);
        Assert.Equal(id, arrival.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_source_is_never_moved_even_after_validation(bool afterValidation)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db);
        await ApproveAsync(db);
        if (afterValidation) await RunImportAsync(db);
        await using (var scope = db.Scope())
        {
            var file = await scope.ServiceProvider.GetRequiredService<FabDbContext>().CollectionFiles.SingleAsync(Token);
            await File.WriteAllTextAsync(file.SourcePath, "different content", Token);
        }
        if (afterValidation) await RunFilingAsync(db); else await RunImportAsync(db);
        Assert.Equal("Inventory", await PhaseAsync(db));
        await using var read = db.Scope();
        var context = read.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Empty(await context.VideoFiles.ToListAsync(Token));
        Assert.Null((await context.CollectionFiles.SingleAsync(Token)).ApprovedAt);
    }

    [Fact]
    public async Task A_new_destination_occupant_stays_untouched_and_requires_a_new_preview()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db);
        await ApproveAsync(db);
        string destination;
        await using (var scope = db.Scope())
        {
            destination = (await scope.ServiceProvider.GetRequiredService<FabDbContext>().CollectionFiles.SingleAsync(Token)).PlannedPath!;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, "someone else's video", Token);
        await RunImportAsync(db);
        Assert.NotEqual("Importing", await PhaseAsync(db));
        await RunFilingAsync(db);
        Assert.Equal("someone else's video", await File.ReadAllTextAsync(destination, Token));
        await using var read = db.Scope();
        Assert.Empty(await read.ServiceProvider.GetRequiredService<FabDbContext>().VideoFiles.ToListAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_move_recovers_from_the_durable_target_with_or_without_the_source(bool sourceWasDeleted)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db);
        await ApproveAsync(db);
        await RunImportAsync(db);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var file = await context.CollectionFiles.SingleAsync(Token);
            var arrival = await context.ArrivingFiles.AsTracking().SingleAsync(Token);
            arrival.IntendedPath = file.PlannedPath;
            arrival.State = ArrivingFileState.Filing;
            await context.SaveChangesAsync(Token);
            Directory.CreateDirectory(Path.GetDirectoryName(file.PlannedPath!)!);
            await scope.ServiceProvider.GetRequiredService<VideoFileMover>().CopyAndVerifyAsync(file.SourcePath, file.PlannedPath!, Token);
            if (sourceWasDeleted) File.Delete(file.SourcePath);
        }
        await RunFilingAsync(db);
        await RunImportAsync(db);
        Assert.Equal("Ready", await PhaseAsync(db));
        await using var read = db.Scope();
        var contextAfter = read.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Single(await contextAfter.VideoFiles.ToListAsync(Token));
        Assert.Single(await contextAfter.OperationLogEntries.Where(row => row.Act == "Filed").ToListAsync(Token));
    }

    private async Task<Guid> SeedAsync(TestDatabase db, bool inLibrary = false)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var library = Path.Combine(root, "library");
        Directory.CreateDirectory(library);
        var path = Path.Combine(inLibrary ? library : root, "existing.mkv");
        await File.WriteAllTextAsync(path, "original file", Token);
        await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.LibraryRoot, library), Token);
        var site = new CatalogueSiteRow { PrdbId = Guid.NewGuid(), Title = "Site" };
        var video = Guid.NewGuid();
        context.CatalogueSites.Add(site);
        context.CatalogueVideos.Add(new() { PrdbId = video, Site = site, Title = "Scene", ReleaseDate = new DateOnly(2026, 10, 1) });
        var id = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        context.CollectionPreparations.Add(new() { Phase = "Planning", StartedAt = DateTimeOffset.UtcNow });
        context.CollectionRoots.Add(new() { Id = rootId, Path = inLibrary ? library : root });
        context.CollectionFiles.Add(new() { Id = id, RootId = rootId, SourcePath = path, SizeBytes = new FileInfo(path).Length,
            LastWriteAt = File.GetLastWriteTimeUtc(path), Probed = true });
        context.ArrivingFiles.Add(new() { Id = id, SourcePath = path, ArrivedName = "existing.mkv", VideoId = video,
            SizeBytes = new FileInfo(path).Length, QualityLabel = "1080p", State = ArrivingFileState.AwaitingFiling });
        await context.SaveChangesAsync(Token);
        await scope.ServiceProvider.GetRequiredService<CollectionPlanningRoutine>().RunAsync(null, Token);
        return id;
    }
    private static async Task ApproveAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        var preview = scope.ServiceProvider.GetRequiredService<CollectionPreview>();
        var page = await preview.ReadAsync(1, Token);
        Assert.True((await preview.ApproveAsync(page.Revision, [], [], true, false, Token)).Accepted);
    }
    private static async Task RunImportAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        await scope.ServiceProvider.GetRequiredService<CollectionImportRoutine>().RunAsync(null, Token);
    }
    private static async Task RunFilingAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        await scope.ServiceProvider.GetRequiredService<FilingRoutine>().RunAsync(null, Token);
    }
    private static async Task<string> PhaseAsync(TestDatabase db)
    {
        await using var scope = db.Scope();
        return (await scope.ServiceProvider.GetRequiredService<FabDbContext>().CollectionPreparations.SingleAsync(Token)).Phase;
    }
}
