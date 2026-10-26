using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class LibraryVerificationRequestTests
{
    [Fact]
    public async Task A_missing_mount_can_be_rechecked_after_it_returns_without_losing_held_records()
    {
        await using var database = await TestDatabase.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv");
        var token = TestContext.Current.CancellationToken;
        try
        {
            await using (var seed = database.Scope())
            {
                var context = seed.ServiceProvider.GetRequiredService<FabDbContext>();
                var video = Guid.NewGuid();
                context.LibraryEntries.Add(new LibraryEntryRow { VideoId = video, EntryDirectory = Path.GetDirectoryName(path)! });
                context.VideoFiles.Add(new VideoFileRow { Id = Guid.NewGuid(), LibraryEntryVideoId = video, FiledPath = path, QualityLabel = "1080p" });
                await context.SaveChangesAsync(token);
                await seed.ServiceProvider.GetRequiredService<LibraryVerificationRoutine>().RunAsync(null, token);
            }
            await File.WriteAllBytesAsync(path, new byte[256 * 1024], token);
            await using (var request = database.Scope())
            {
                var service = request.ServiceProvider.GetRequiredService<LibraryVerificationRequests>();
                Assert.Equal(1, (await service.ReadAsync(token)).Unconfirmed);
                Assert.Equal(1, (await service.RequestAsync(true, token)).Pending);
                Assert.Equal(1, (await service.RequestAsync(true, token)).Pending);
            }
            // A fresh scope resumes only the database's work set.
            await using var resumed = database.Scope();
            await resumed.ServiceProvider.GetRequiredService<LibraryVerificationRoutine>().RunAsync(null, token);
            var progress = await resumed.ServiceProvider.GetRequiredService<LibraryVerificationRequests>().ReadAsync(token);
            Assert.Equal(new LibraryVerificationProgress(1, 0, 0), progress);
            var read = resumed.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(1, await read.LibraryEntries.CountAsync(token));
            Assert.Empty(await read.Downloads.ToListAsync(token));
            Assert.True(File.Exists(path));
            Assert.Equal(0, (await resumed.ServiceProvider.GetRequiredService<LibraryVerificationRequests>().RequestAsync(true, token)).Pending);
            Assert.Equal(1, (await resumed.ServiceProvider.GetRequiredService<LibraryVerificationRequests>().RequestAsync(false, token)).Pending);
        }
        finally { File.Delete(path); }
    }
}
