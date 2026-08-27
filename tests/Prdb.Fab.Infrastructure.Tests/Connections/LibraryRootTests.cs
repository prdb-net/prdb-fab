using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Connections;

/// <summary>
/// ADR 0010's library-root step against a real database and real directories:
/// one path, and three checks on it.
/// </summary>
public sealed class LibraryRootTests : IAsyncDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-tests", Guid.NewGuid().ToString("n"));

    private TestDatabase? database;

    [Fact]
    public async Task A_writable_directory_with_no_downloads_beside_it_is_stored()
    {
        var library = await DirectoryNamed("library");
        var saved = await SaveAsync(library);

        Assert.Equal(LibraryRootOutcome.Saved, saved.Outcome);
        Assert.Equal(library, await StoredRootAsync());
    }

    [Fact]
    public async Task A_directory_that_is_not_there_is_refused_and_nothing_is_stored()
    {
        var saved = await SaveAsync(Path.Combine(directory, "not-mounted"));

        Assert.Equal(LibraryRootOutcome.Missing, saved.Outcome);
        Assert.Null(await StoredRootAsync());
    }

    [Fact]
    public async Task A_relative_path_is_refused() =>
        Assert.Equal(LibraryRootOutcome.NotAbsolute, (await SaveAsync("library")).Outcome);

    /// <summary>
    /// The overlap, in the direction where the library is underneath what
    /// SABnzbd finished into. Filing moves videos out of there, and it cannot
    /// move them into the place it is moving them out of.
    /// </summary>
    [Fact]
    public async Task A_library_inside_the_download_directory_is_refused()
    {
        var downloads = await DirectoryNamed("downloads");
        await MapDownloadsTo(downloads);

        var library = await DirectoryNamed(Path.Combine("downloads", "library"));

        Assert.Equal(LibraryRootOutcome.InsideDownloadDirectory, (await SaveAsync(library)).Outcome);
        Assert.Null(await StoredRootAsync());
    }

    /// <summary>And the other direction, which is the one that is easy to forget.</summary>
    [Fact]
    public async Task A_library_containing_the_download_directory_is_refused()
    {
        var library = await DirectoryNamed("library");
        var downloads = await DirectoryNamed(Path.Combine("library", "downloads"));

        await MapDownloadsTo(downloads);

        Assert.Equal(LibraryRootOutcome.ContainsDownloadDirectory, (await SaveAsync(library)).Outcome);
        Assert.Null(await StoredRootAsync());
    }

    /// <summary>
    /// Both on one volume, which is the case CI can produce. What is being
    /// checked is that the ordinary answer is the plain one, so that the warning
    /// means something when it does appear.
    /// </summary>
    [Fact]
    public async Task Downloads_on_the_same_filesystem_draw_no_warning()
    {
        var downloads = await DirectoryNamed("downloads");
        await MapDownloadsTo(downloads);

        var library = await DirectoryNamed("library");

        Assert.Equal(LibraryRootOutcome.Saved, (await SaveAsync(library)).Outcome);
    }

    /// <summary>
    /// ADR 0010 refuses to refuse this: some NAS layouts genuinely put the
    /// library and the downloads on different filesystems, and refusing them
    /// would be refusing a working installation. So it is a verdict the form
    /// shows and continues past.
    /// </summary>
    /// <remarks>
    /// Two real mounts rather than a stand-in, and Linux is the only place this
    /// can ask for a second one it did not create — <c>/dev/shm</c> is its own
    /// filesystem there, and the container ADR 0034 ships is Linux. Where the
    /// platform has only one mount to offer, this says so rather than passing:
    /// ADR 0042 already declined to mount a loop device to manufacture the
    /// kernel's own cross-device refusal, and this is the same trade.
    /// </remarks>
    [Fact]
    public async Task Downloads_on_another_filesystem_are_a_warning_rather_than_a_refusal()
    {
        const string OtherFilesystem = "/dev/shm";

        if (!OperatingSystem.IsLinux() || !Directory.Exists(OtherFilesystem))
        {
            Assert.Skip($"This platform has no second filesystem at {OtherFilesystem} to compare against.");
        }

        var downloads = Path.Combine(OtherFilesystem, "prdb-fab-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(downloads);

        try
        {
            if (Directories.OnTheSameFilesystem(directory, downloads) is not false)
            {
                Assert.Skip($"{OtherFilesystem} is not a separate mount here.");
            }

            await MapDownloadsTo(downloads);

            var library = await DirectoryNamed("library");

            Assert.Equal(LibraryRootOutcome.SavedWithWarning, (await SaveAsync(library)).Outcome);

            // Continued past, which is the half that makes it a warning.
            Assert.Equal(library, await StoredRootAsync());
        }
        finally
        {
            Directory.Delete(downloads, recursive: true);
        }
    }

    private async Task<LibraryRootSave> SaveAsync(string path)
    {
        await using var scope = (await DatabaseAsync()).Scope();

        return await scope.ServiceProvider.GetRequiredService<LibraryRoots>()
            .SaveAsync(path, TestContext.Current.CancellationToken);
    }

    private async Task MapDownloadsTo(string path)
    {
        await using var scope = (await DatabaseAsync()).Scope();

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.SingleAsync(TestContext.Current.CancellationToken);

        installation.PathMappingFrom = "/downloads/complete";
        installation.PathMappingTo = path;

        // Reads do not track (ADR 0039), which is why every write in this
        // project says so explicitly.
        context.Installation.Update(installation);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> StoredRootAsync()
    {
        await using var scope = (await DatabaseAsync()).Scope();

        return (await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken)).LibraryRoot;
    }

    private Task<string> DirectoryNamed(string relative)
    {
        var path = Path.Combine(directory, relative);
        Directory.CreateDirectory(path);

        return Task.FromResult(path);
    }

    private async Task<TestDatabase> DatabaseAsync() => database ??= await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        if (database is not null)
        {
            await database.DisposeAsync();
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
