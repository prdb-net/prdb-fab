using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class LibraryEntryDeletionTests
{
    [Fact]
    public async Task Delete_confirms_every_file_removes_owned_entry_files_and_records_each_act()
    {
        var directory = TemporaryDirectory();
        var firstPath = Path.Combine(directory, "first.mkv");
        var secondPath = Path.Combine(directory, "second.mp4");
        var sidecar = Path.Combine(directory, EntryPath.SidecarFileName);
        var image = Path.Combine(directory, EntryPath.EntryImageFileName);
        var unexpected = Path.Combine(directory, "keep.txt");
        await File.WriteAllBytesAsync(firstPath, new byte[17], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(secondPath, new byte[23], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sidecar, "sidecar", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(image, new byte[31], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(unexpected, "not owned by this action", TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var videoId = Guid.NewGuid();
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            await SeedAsync(database, videoId, directory,
                FileRow(firstId, videoId, firstPath, 17, "1080p"),
                FileRow(secondId, videoId, secondPath, 23, "2160p"));

            await using (var scope = database.Scope())
            {
                var deletion = scope.ServiceProvider.GetRequiredService<LibraryEntryDeletion>();
                var preview = await deletion.PreviewAsync(videoId, TestContext.Current.CancellationToken);
                Assert.NotNull(preview);
                Assert.Equal(LibraryEntryDeleteOutcome.Ready, preview!.Outcome);
                Assert.Equal(new[] { firstId, secondId }.Order(), preview.Files.Select(file => file.Id).Order());
                Assert.Equal(["first.mkv", "second.mp4"], preview.Files.Select(file => file.FileName));

                var verdict = await deletion.DeleteAsync(
                    videoId,
                    preview.Files.Select(file => file.Id).ToArray(),
                    TestContext.Current.CancellationToken);
                Assert.NotNull(verdict);
                Assert.Equal(LibraryEntryDeleteOutcome.Deleted, verdict!.Outcome);
                Assert.Equal(2, verdict.DeletedFiles);
            }

            Assert.False(File.Exists(firstPath));
            Assert.False(File.Exists(secondPath));
            Assert.False(File.Exists(sidecar));
            Assert.False(File.Exists(image));
            Assert.True(File.Exists(unexpected));
            Assert.True(Directory.Exists(directory));

            await using var check = database.Scope();
            var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Empty(await context.LibraryEntries.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.VideoFiles.ToListAsync(TestContext.Current.CancellationToken));
            var operations = await context.OperationLogEntries
                .OrderBy(row => row.PathBefore)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, operations.Count);
            Assert.All(operations, operation =>
            {
                Assert.Equal("Deleted", operation.Act);
                Assert.Equal("Person", operation.Actor);
                Assert.Equal("Library Entry deleted", operation.Reason);
                Assert.Equal(videoId, operation.VideoId);
                Assert.Equal(videoId, operation.LibraryEntryVideoId);
            });
            Assert.Equal([firstPath, secondPath], operations.Select(operation => operation.PathBefore));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_refuses_when_the_confirmed_entry_or_a_file_has_changed()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "changed.mkv");
        await File.WriteAllBytesAsync(path, new byte[18], TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var videoId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            await SeedAsync(database, videoId, directory, FileRow(fileId, videoId, path, 17, "1080p"));

            await using var scope = database.Scope();
            var deletion = scope.ServiceProvider.GetRequiredService<LibraryEntryDeletion>();
            var wrongSelection = await deletion.DeleteAsync(videoId, [Guid.NewGuid()], TestContext.Current.CancellationToken);
            var changedFile = await deletion.DeleteAsync(videoId, [fileId], TestContext.Current.CancellationToken);

            Assert.Equal(LibraryEntryDeleteOutcome.EntryChanged, wrongSelection!.Outcome);
            Assert.Equal(LibraryEntryDeleteOutcome.EntryChanged, changedFile!.Outcome);
            Assert.True(File.Exists(path));
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Single(await context.LibraryEntries.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Single(await context.VideoFiles.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.OperationLogEntries.ToListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task SeedAsync(
        TestDatabase database,
        Guid videoId,
        string directory,
        params VideoFileRow[] files)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = videoId,
            EntryDirectory = directory,
            FiledAt = database.Time.GetUtcNow(),
        });
        context.VideoFiles.AddRange(files);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static VideoFileRow FileRow(
        Guid id,
        Guid videoId,
        string path,
        long size,
        string quality) => new()
    {
        Id = id,
        LibraryEntryVideoId = videoId,
        FiledPath = path,
        QualityLabel = quality,
        SizeBytes = size,
    };

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "prdb-fab-library-entry-deletion",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
