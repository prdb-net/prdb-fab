using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Hashing;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class CollectionUndoTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fab-return-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] bytes = Enumerable.Range(0, 256 * 1024).Select(index => (byte)(index % 251)).ToArray();

    [Fact]
    public async Task Approval_moves_nothing_and_a_new_scope_returns_content_and_holds_automation_until_resumed()
    {
        await using var database = await SeedAsync();
        await ApproveAsync(database);
        await using (var pending = database.Scope())
        {
            await pending.ServiceProvider.GetRequiredService<CollectionScanRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal("Undoing", (await pending.ServiceProvider.GetRequiredService<FabDbContext>().CollectionPreparations.SingleAsync(TestContext.Current.CancellationToken)).Phase);
        }
        Assert.True(File.Exists(Source));
        Assert.False(File.Exists(Destination));
        await RunAsync(database);
        Assert.False(File.Exists(Source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Empty(await context.VideoFiles.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.LibraryEntries.ToListAsync(TestContext.Current.CancellationToken));
        await scope.ServiceProvider.GetRequiredService<CollectionScanRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<CollectionPlanningRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<CollectionImportRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal("UndoComplete", (await context.CollectionPreparations.SingleAsync(TestContext.Current.CancellationToken)).Phase);
        Assert.False((await scope.ServiceProvider.GetRequiredService<CollectionPreparation>().RetryAsync(TestContext.Current.CancellationToken)).Accepted);
        Assert.Single(await context.OperationLogEntries.Where(row => row.Act == "Returned").ToListAsync(TestContext.Current.CancellationToken));
        Assert.True((await scope.ServiceProvider.GetRequiredService<CollectionUndo>().ResumeAsync(false, TestContext.Current.CancellationToken)).Accepted);
        Assert.Equal("Ready", (await context.CollectionPreparations.SingleAsync(TestContext.Current.CancellationToken)).Phase);
    }

    [Fact]
    public async Task A_conflict_never_overwrites_content_and_does_not_prevent_retry_after_the_conflict_is_removed()
    {
        await using var database = await SeedAsync();
        await File.WriteAllTextAsync(Destination, "existing unrelated file", TestContext.Current.CancellationToken);
        await using (var scope = database.Scope())
            Assert.NotNull(Assert.Single((await scope.ServiceProvider.GetRequiredService<CollectionUndo>().ReadAsync(1, TestContext.Current.CancellationToken)).Files).Conflict);
        await ApproveAsync(database);
        await RunAsync(database);
        Assert.Equal("existing unrelated file", await File.ReadAllTextAsync(Destination, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Source));
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var file = await context.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            var delete = await scope.ServiceProvider.GetRequiredService<LibraryEntryDeletion>()
                .DeleteAsync(file.LibraryEntryVideoId, [file.Id], TestContext.Current.CancellationToken);
            Assert.Equal(LibraryEntryDeleteOutcome.EntryChanged, delete!.Outcome);
            Assert.NotNull((await context.CollectionUndos.SingleAsync(TestContext.Current.CancellationToken)).Error);
        }
        File.Delete(Destination);
        database.Time.Advance(TimeSpan.FromMinutes(2));
        await RunAsync(database);
        Assert.False(File.Exists(Source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_interrupted_return_recovers_only_when_the_entire_destination_matches(bool corruptMiddle)
    {
        await using var database = await SeedAsync();
        await ApproveAsync(database);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var row = await context.CollectionUndos.AsTracking().SingleAsync(TestContext.Current.CancellationToken);
            row.StartedAt = database.Time.GetUtcNow();
            row.ContentHash = Convert.ToHexString(SHA256.HashData(bytes));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        File.Move(Source, Destination);
        if (corruptMiddle)
        {
            var altered = (byte[])bytes.Clone();
            altered[128 * 1024] ^= 0xff;
            await File.WriteAllBytesAsync(Destination, altered, TestContext.Current.CancellationToken);
        }
        await RunAsync(database);
        await using var read = database.Scope();
        var db = read.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal(corruptMiddle ? 1 : 0, await db.VideoFiles.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(corruptMiddle, (await db.CollectionUndos.SingleAsync(TestContext.Current.CancellationToken)).CompletedAt is null);
        Assert.True(File.Exists(Destination));
    }

    [Fact]
    public async Task An_unapproved_restored_return_waits_until_paths_are_explicitly_confirmed()
    {
        await using var database = await SeedAsync();
        await ApproveAsync(database);
        await using (var scope = database.Scope())
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().CollectionUndos
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Approved, false), TestContext.Current.CancellationToken);
        await RunAsync(database);
        Assert.True(File.Exists(Source));
        await using (var scope = database.Scope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<CollectionUndo>().ResumeAsync(true, TestContext.Current.CancellationToken)).Accepted);
        await RunAsync(database);
        Assert.False(File.Exists(Source));
    }

    [Fact]
    public async Task A_changed_Library_path_invalidates_the_preview_even_without_a_new_collection_revision()
    {
        await using var database = await SeedAsync();
        await using var scope = database.Scope();
        var undo = scope.ServiceProvider.GetRequiredService<CollectionUndo>();
        var preview = await undo.ReadAsync(1, TestContext.Current.CancellationToken);
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.VideoFiles.ExecuteUpdateAsync(update => update.SetProperty(row => row.FiledPath, Source + ".renamed"), TestContext.Current.CancellationToken);
        Assert.False((await undo.RequestAsync(preview.Revision, preview.Evidence, [], true, TestContext.Current.CancellationToken)).Accepted);
        Assert.Empty(await context.CollectionUndos.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Whole_preview_approval_includes_files_beyond_the_visible_page()
    {
        await using var database = await SeedAsync();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var rootRow = await context.CollectionRoots.SingleAsync(TestContext.Current.CancellationToken);
        var first = await context.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        for (var index = 1; index < 55; index++)
        {
            var fileId = Guid.NewGuid();
            var original = Destination + index;
            context.CollectionFiles.Add(new CollectionFileRow { Id = Guid.NewGuid(), RootId = rootRow.Id, SourcePath = original });
            context.VideoFiles.Add(new VideoFileRow { Id = fileId, LibraryEntryVideoId = first.LibraryEntryVideoId,
                FiledPath = Source + index, QualityLabel = "test-" + index });
            context.OperationLogEntries.Add(new OperationLogEntryRow { Id = Guid.NewGuid(), Act = "Filed", PathBefore = original, VideoFileId = fileId });
        }
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<CollectionUndo>();
        var preview = await service.ReadAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal(55, preview.Available);
        Assert.Equal(50, preview.Files.Count);
        Assert.Equal(5, (await service.ReadAsync(2, TestContext.Current.CancellationToken)).Files.Count);
        Assert.True((await service.RequestAsync(preview.Revision, preview.Evidence, [], true, TestContext.Current.CancellationToken)).Accepted);
        Assert.Equal(55, await context.CollectionUndos.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Backup_carries_the_return_evidence_and_Restore_re_roots_it_without_reauthorizing_moves()
    {
        await using var database = await SeedAsync();
        await ApproveAsync(database);
        string backup;
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.LibraryRoot, Path.Combine(root, "library"))
                .SetProperty(row => row.NotificationsEnabled, true)
                .SetProperty(row => row.NotificationWebhookUrl, "https://receiver.example/private"), TestContext.Current.CancellationToken);
            backup = BackupJson.Write(await scope.ServiceProvider.GetRequiredService<Backups>().ReadAsync(TestContext.Current.CancellationToken));
        }
        var newLibrary = Path.Combine(root, "restored-library");
        Directory.CreateDirectory(newLibrary);
        await using var restored = await TestDatabase.CreateAsync();
        await using var target = restored.Scope();
        var verdict = await target.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(backup,
            new RestoreRoots(newLibrary, null), TestContext.Current.CancellationToken);
        Assert.Equal(RestoreOutcome.Restored, verdict.Outcome);
        var read = target.ServiceProvider.GetRequiredService<FabDbContext>();
        var returned = await read.CollectionUndos.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(returned.Approved);
        Assert.Equal(Path.Combine(newLibrary, "entry", "file.mkv"), returned.SourcePath);
        Assert.Equal(Destination, returned.DestinationPath);
        Assert.Equal("Undoing", (await read.CollectionPreparations.SingleAsync(TestContext.Current.CancellationToken)).Phase);
        var installation = await read.Installation.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(installation.NotificationsEnabled);
        Assert.Equal("https://receiver.example/private", installation.NotificationWebhookUrl);
        await target.ServiceProvider.GetRequiredService<CollectionUndoRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Source));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Files_reorganised_inside_the_Library_can_return_while_other_qualities_stay_held()
    {
        await using var database = await SeedAsync();
        var original = Path.Combine(root, "library", "original.mkv");
        var other = Path.Combine(Path.GetDirectoryName(Source)!, "other.mkv");
        await File.WriteAllBytesAsync(other, bytes, TestContext.Current.CancellationToken);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.CollectionRoots.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLibrary, true)
                .SetProperty(row => row.Path, Path.Combine(root, "library")), TestContext.Current.CancellationToken);
            await context.CollectionFiles.ExecuteUpdateAsync(update => update.SetProperty(row => row.SourcePath, original), TestContext.Current.CancellationToken);
            await context.OperationLogEntries.ExecuteUpdateAsync(update => update.SetProperty(row => row.PathBefore, original), TestContext.Current.CancellationToken);
            var first = await context.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
            context.VideoFiles.Add(new VideoFileRow { Id = Guid.NewGuid(), LibraryEntryVideoId = first.LibraryEntryVideoId,
                FiledPath = other, QualityLabel = "720p", SizeBytes = bytes.Length });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await ApproveAsync(database);
        await RunAsync(database);
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(other));
        await using var read = database.Scope();
        Assert.Equal(1, await read.ServiceProvider.GetRequiredService<FabDbContext>().LibraryEntries.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_scan_root_replaced_by_a_symlink_never_redirects_an_approved_return()
    {
        await using var database = await SeedAsync();
        await ApproveAsync(database);
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        Directory.Delete(Path.GetDirectoryName(Destination)!);
        Directory.CreateSymbolicLink(Path.GetDirectoryName(Destination)!, outside);
        await RunAsync(database);
        Assert.True(File.Exists(Source));
        Assert.Empty(Directory.GetFiles(outside));
    }

    private string Source => Path.Combine(root, "library", "entry", "file.mkv");
    private string Destination => Path.Combine(root, "original", "file.mkv");

    private async Task<TestDatabase> SeedAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        await File.WriteAllBytesAsync(Source, bytes, TestContext.Current.CancellationToken);
        Assert.True(OsHash.TryCompute(Source, out var hash));
        var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var rootId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var video = Guid.NewGuid();
        context.CollectionPreparations.Add(new CollectionPreparationRow { Phase = "Ready" });
        context.CollectionRoots.Add(new CollectionRootRow { Id = rootId, Path = Path.GetDirectoryName(Destination)! });
        context.CollectionFiles.Add(new CollectionFileRow { Id = Guid.NewGuid(), RootId = rootId, SourcePath = Destination, SizeBytes = bytes.Length });
        context.LibraryEntries.Add(new LibraryEntryRow { VideoId = video, EntryDirectory = Path.GetDirectoryName(Source)! });
        context.VideoFiles.Add(new VideoFileRow { Id = fileId, LibraryEntryVideoId = video, FiledPath = Source, SizeBytes = bytes.Length, OsHash = hash, QualityLabel = "1080p" });
        context.OperationLogEntries.Add(new OperationLogEntryRow { Id = Guid.NewGuid(), Act = "Filed", PathBefore = Destination, PathAfter = Source, VideoFileId = fileId, VideoId = video });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private static async Task ApproveAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        var undo = scope.ServiceProvider.GetRequiredService<CollectionUndo>();
        var preview = await undo.ReadAsync(1, TestContext.Current.CancellationToken);
        Assert.Equal(1, preview.Available);
        Assert.True((await undo.RequestAsync(preview.Revision, preview.Evidence, [], true, TestContext.Current.CancellationToken)).Accepted);
    }

    private static async Task RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<CollectionUndoRoutine>().RunAsync(null, TestContext.Current.CancellationToken);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
