using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Backup;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_rechecks_inventory_and_resumes_only_the_unchanged_approved_move(bool targetAlreadyMoved)
    {
        await using var source = await TestDatabase.CreateAsync();
        await SeedAsync(source);
        await ApproveAsync(source);
        if (targetAlreadyMoved) await RunImportAsync(source);
        BackupDocument document;
        CatalogueVideoRow video;
        await using (var scope = source.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            video = await context.CatalogueVideos.Include(row => row.Site).SingleAsync(Token);
            if (targetAlreadyMoved)
            {
                var arrival = await context.ArrivingFiles.AsTracking().SingleAsync(Token);
                arrival.State = ArrivingFileState.Filing;
                arrival.IntendedPath = (await context.CollectionFiles.SingleAsync(Token)).PlannedPath;
                Directory.CreateDirectory(Path.GetDirectoryName(arrival.IntendedPath!)!);
                File.Move(arrival.SourcePath, arrival.IntendedPath!);
                await context.SaveChangesAsync(Token);
            }
            document = await scope.ServiceProvider.GetRequiredService<Backups>().ReadAsync(Token);
        }
        await using var restored = await TestDatabase.CreateAsync();
        await using (var scope = restored.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(BackupJson.Write(document), new RestoreRoots(Path.Combine(root, "library"), null), Token);
            Assert.Equal(RestoreOutcome.Restored, result.Outcome);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal("Inventory", (await context.CollectionPreparations.SingleAsync(Token)).Phase);
            var site = new CatalogueSiteRow { PrdbId = video.Site!.PrdbId, Title = video.Site.Title };
            context.CatalogueSites.Add(site);
            context.CatalogueVideos.Add(new() { PrdbId = video.PrdbId, Title = video.Title, ReleaseDate = video.ReleaseDate, Site = site });
            await context.SaveChangesAsync(Token);
            Assert.Equal(Prdb.Fab.Core.Scheduling.RunResult.NothingToDo,
                await scope.ServiceProvider.GetRequiredService<FilingRoutine>().RunAsync(null, Token));
        }
        for (var i = 0; i < 8; i++)
        {
            await using var scope = restored.Scope();
            await scope.ServiceProvider.GetRequiredService<CollectionScanRoutine>().RunAsync(null, Token);
            await scope.ServiceProvider.GetRequiredService<CollectionPlanningRoutine>().RunAsync(null, Token);
            await scope.ServiceProvider.GetRequiredService<CollectionImportRoutine>().RunAsync(null, Token);
        }
        Assert.Equal("Importing", await PhaseAsync(restored));
        await RunFilingAsync(restored);
        await RunImportAsync(restored);
        Assert.Equal("Ready", await PhaseAsync(restored));
        await using var read = restored.Scope();
        Assert.Single(await read.ServiceProvider.GetRequiredService<FabDbContext>().VideoFiles.ToListAsync(Token));
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
