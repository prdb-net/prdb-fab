using System.Xml.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Hashing;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class LibraryWritingTests
{
    private static readonly Guid Video = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000201");
    private static readonly Guid OtherVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000202");
    private static readonly Guid Site = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000201");
    private static readonly Guid Actor = Guid.Parse("cccccccc-0000-4000-8000-000000000201");
    private static readonly Guid Image = Guid.Parse("dddddddd-0000-4000-8000-000000000201");
    private static readonly Guid Download = Guid.Parse("0198ec28-1c00-7000-8000-000000000201");
    private static readonly Guid Indexer = Guid.Parse("0198ec28-1c00-7000-8000-000000000202");

    [Fact]
    public async Task Sidecar_and_cached_Entry_Image_are_complete_atomic_outputs()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            await using var scope = database.Scope();
            var artwork = scope.ServiceProvider.GetRequiredService<ArtworkStore>();
            await artwork.WriteAsync(Image, [1, 2, 3, 4], TestContext.Current.CancellationToken);
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var video = await context.CatalogueVideos.SingleAsync(
                row => row.PrdbId == Video,
                TestContext.Current.CancellationToken);
            context.CatalogueImages.Add(new CatalogueImageRow
            {
                PrdbId = Image,
                VideoId = video.Id,
                Url = "https://example.invalid/image.jpg",
                Cached = true,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var writer = scope.ServiceProvider.GetRequiredService<EntryFiles>();
            await writer.WriteAsync(
                root,
                Video,
                TestContext.Current.CancellationToken);

            var movie = XDocument.Load(Path.Combine(root, "movie.nfo"));
            Assert.Equal("A Title", movie.Root!.Element("title")!.Value);
            Assert.Equal("2026-08-28", movie.Root.Element("premiered")!.Value);
            Assert.Equal("A Site", movie.Root.Element("studio")!.Value);
            var actor = Assert.Single(movie.Root.Elements("actor"));
            Assert.Equal("An Actor", actor.Element("name")!.Value);
            Assert.Equal("Actor", actor.Element("type")!.Value);
            Assert.Equal(Video.ToString("D"), movie.Root.Element("uniqueid")!.Value);
            Assert.Equal("prdb", movie.Root.Element("uniqueid")!.Attribute("type")!.Value);
            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(
                Path.Combine(root, "fanart.jpg"),
                TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(root, ".*.part"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_artwork_omits_the_image_without_blocking_the_sidecar()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            await using var scope = database.Scope();
            await scope.ServiceProvider.GetRequiredService<EntryFiles>().WriteAsync(
                root,
                Video,
                TestContext.Current.CancellationToken);

            var movie = XDocument.Load(Path.Combine(root, "movie.nfo"));
            Assert.Equal("2026-08-28", movie.Root!.Element("premiered")!.Value);
            Assert.Single(movie.Root.Elements("actor"));
            Assert.False(File.Exists(Path.Combine(root, "fanart.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_expensive_move_branch_copies_verifies_renames_and_deletes()
    {
        var root = TemporaryDirectory();
        var source = Path.Combine(root, "source.mkv");
        var target = Path.Combine(root, "target.mkv");
        var temporary = Path.Combine(root, ".filing-test.part");
        var bytes = Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251)).ToArray();
        await File.WriteAllBytesAsync(source, bytes, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(temporary, "interrupted", TestContext.Current.CancellationToken);

        try
        {
            var mover = new VideoFileMover();
            await mover.MoveAsync(
                source,
                target,
                temporary,
                FilingMove.CopyVerifyDelete,
                TestContext.Current.CancellationToken);

            Assert.False(File.Exists(source));
            Assert.False(File.Exists(temporary));
            Assert.True(await mover.SameBytesAsync(
                target,
                await WriteComparisonAsync(root, bytes),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task First_Filing_records_the_once_computed_path_and_writes_the_video_last()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var source = await VideoFileAsync(root, "arrival.mkv", 300_000, 17);
            await AddArrivalAsync(database, Video, source, "1080p");

            await RunAsync(database);

            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var entry = await context.LibraryEntries.SingleAsync(TestContext.Current.CancellationToken);
            var expectedDirectory = Path.Combine(root, "A Site", "A Site - 2026-08-28 - A Title");
            var expectedFile = Path.Combine(expectedDirectory, "A Site - 2026-08-28 - A Title.mkv");
            Assert.Equal(expectedDirectory, entry.EntryDirectory);
            Assert.Equal(expectedFile, (await context.VideoFiles.SingleAsync(
                TestContext.Current.CancellationToken)).FiledPath);
            Assert.True(File.Exists(expectedFile));
            Assert.True(File.Exists(Path.Combine(expectedDirectory, "movie.nfo")));
            Assert.False(File.Exists(source));
            Assert.Equal(ArrivingFileState.Filed, (await context.ArrivingFiles.SingleAsync(
                TestContext.Current.CancellationToken)).State);
            Assert.Equal("Filed", (await context.OperationLogEntries.SingleAsync(
                TestContext.Current.CancellationToken)).Act);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_occupied_name_uses_the_full_video_id_and_two_collisions_touch_nothing()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var ordinary = EntryDirectory(root);
            Directory.CreateDirectory(ordinary);
            var stranger = Path.Combine(ordinary, "stranger.txt");
            await File.WriteAllTextAsync(stranger, "keep", TestContext.Current.CancellationToken);
            var source = await VideoFileAsync(root, "collision.mkv", 300_000, 18);
            await AddArrivalAsync(database, Video, source, "1080p");

            await RunAsync(database);

            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var entry = await context.LibraryEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.EndsWith($" [{Video:D}]", entry.EntryDirectory, StringComparison.Ordinal);
            Assert.Equal("keep", await File.ReadAllTextAsync(stranger, TestContext.Current.CancellationToken));

            var secondRoot = TemporaryDirectory();
            try
            {
                await using var second = await PreparedAsync(secondRoot);
                var first = EntryDirectory(secondRoot);
                var distinguished = EntryDirectory(secondRoot, distinguish: true);
                Directory.CreateDirectory(first);
                Directory.CreateDirectory(distinguished);
                await File.WriteAllTextAsync(Path.Combine(first, "one"), "one", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(distinguished, "two"), "two", TestContext.Current.CancellationToken);
                var blocked = await VideoFileAsync(secondRoot, "blocked.mkv", 300_000, 19);
                await AddArrivalAsync(second, Video, blocked, "1080p");

                await Assert.ThrowsAsync<IOException>(() => RunAsync(second));
                Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(first, "one"), TestContext.Current.CancellationToken));
                Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(distinguished, "two"), TestContext.Current.CancellationToken));
                Assert.True(File.Exists(blocked));
            }
            finally
            {
                Directory.Delete(secondRoot, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_second_Quality_relabels_the_first_and_logs_both_acts()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var entryDirectory = EntryDirectory(root);
            Directory.CreateDirectory(entryDirectory);
            var held = Path.Combine(entryDirectory, "A Site - 2026-08-28 - A Title.mkv");
            await File.WriteAllBytesAsync(held, Bytes(300_000, 20), TestContext.Current.CancellationToken);
            await AddHeldAsync(database, Video, entryDirectory, held, "2160p");
            var source = await VideoFileAsync(root, "second.mp4", 300_000, 21);
            await AddArrivalAsync(database, Video, source, "1080p");

            await RunAsync(database);

            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var files = await context.VideoFiles.OrderBy(row => row.QualityLabel).ToListAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, files.Count);
            Assert.All(files, file => Assert.EndsWith(
                $" - [{file.QualityLabel}]{Path.GetExtension(file.FiledPath)}",
                file.FiledPath,
                StringComparison.Ordinal));
            Assert.Equal(
                ["Filed", "Relabelled"],
                await context.OperationLogEntries.Select(row => row.Act).OrderBy(value => value).ToArrayAsync(
                    TestContext.Current.CancellationToken));
            Assert.False(File.Exists(held));
            Assert.All(files, file => Assert.True(File.Exists(file.FiledPath)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Identical_Duplicate_and_EntryMissing_are_first_reasons_and_leave_sources_untouched()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var entryDirectory = Path.Combine(root, "held");
            Directory.CreateDirectory(entryDirectory);
            var held = await VideoFileAsync(entryDirectory, "held.mkv", 300_000, 22);
            await AddHeldAsync(database, Video, entryDirectory, held, "1080p");

            var identical = await VideoFileAsync(root, "identical.mkv", 300_000, 22);
            await AddArrivalAsync(database, OtherVideo, identical, "2160p");
            await RunAsync(database);
            Assert.Equal(ArrivingFileReason.IdenticalFile, await ReasonAsync(database, identical));
            Assert.True(File.Exists(identical));

            var duplicate = await VideoFileAsync(root, "duplicate.mkv", 300_000, 23);
            await AddArrivalAsync(database, Video, duplicate, "1080p");
            await RunAsync(database);
            Assert.Equal(ArrivingFileReason.Duplicate, await ReasonAsync(database, duplicate));
            Assert.True(File.Exists(duplicate));

            Directory.Delete(entryDirectory, recursive: true);
            var missing = await VideoFileAsync(root, "missing.mkv", 300_000, 24);
            await AddArrivalAsync(database, Video, missing, "720p");
            await RunAsync(database);
            Assert.Equal(ArrivingFileReason.EntryMissing, await ReasonAsync(database, missing));
            Assert.True(File.Exists(missing));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_stale_recorded_hash_does_not_make_changed_library_bytes_identical()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var entryDirectory = Path.Combine(root, "held");
            Directory.CreateDirectory(entryDirectory);
            var held = await VideoFileAsync(entryDirectory, "held.mkv", 300_000, 30);
            await AddHeldAsync(database, Video, entryDirectory, held, "1080p");

            var source = await VideoFileAsync(root, "new.mkv", 300_000, 30);
            await File.WriteAllBytesAsync(
                held,
                Bytes(300_000, 31),
                TestContext.Current.CancellationToken);
            await AddArrivalAsync(database, OtherVideo, source, "2160p");

            await RunAsync(database);

            await using var scope = database.Scope();
            var arrival = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .ArrivingFiles.SingleAsync(
                    row => row.SourcePath == source,
                    TestContext.Current.CancellationToken);
            Assert.Equal(ArrivingFileState.Filed, arrival.State);
            Assert.Null(arrival.Reason);
            Assert.False(File.Exists(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_missing_library_root_is_an_installation_failure_not_EntryMissing()
    {
        var root = TemporaryDirectory();
        await using var database = await PreparedAsync(root);
        var source = await VideoFileAsync(root, "waiting.mkv", 300_000, 25);
        await AddArrivalAsync(database, Video, source, "1080p");
        Directory.Delete(root, recursive: true);

        await Assert.ThrowsAsync<IOException>(() => RunAsync(database));

        await using var scope = database.Scope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles.SingleAsync(
            TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public async Task Recovery_finishes_source_only_target_only_and_partial_temporary_states()
    {
        foreach (var targetAlreadyThere in new[] { false, true })
        {
            var root = TemporaryDirectory();
            try
            {
                await using var database = await PreparedAsync(root);
                var source = await VideoFileAsync(
                    root,
                    "recovery.mkv",
                    300_000,
                    (byte)(targetAlreadyThere ? 26 : 27));
                var intendedDirectory = EntryDirectory(root);
                var recorded = EntryPath.At(intendedDirectory, Path.GetExtension(source));
                var intended = Path.Combine(intendedDirectory, recorded.VideoFileName);
                Directory.CreateDirectory(intendedDirectory);
                if (targetAlreadyThere)
                {
                    File.Move(source, intended);
                }
                else
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(intendedDirectory, FiledPaths.TemporaryName(Download)),
                        "partial",
                        TestContext.Current.CancellationToken);
                }

                await AddArrivalAsync(
                    database,
                    Video,
                    source,
                    "1080p",
                    ArrivingFileState.Filing,
                    intended);

                await RunAsync(database);

                await using var scope = database.Scope();
                var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
                Assert.Equal(ArrivingFileState.Filed, (await context.ArrivingFiles.SingleAsync(
                    TestContext.Current.CancellationToken)).State);
                Assert.True(File.Exists(intended));
                Assert.False(File.Exists(source));
                Assert.False(File.Exists(Path.Combine(intendedDirectory, FiledPaths.TemporaryName(Download))));
                Assert.Single(await context.VideoFiles.ToListAsync(TestContext.Current.CancellationToken));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Recovery_repairs_a_relabel_that_reached_disk_before_its_record()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var entryDirectory = EntryDirectory(root);
            Directory.CreateDirectory(entryDirectory);
            var held = Path.Combine(entryDirectory, "A Site - 2026-08-28 - A Title.mkv");
            await File.WriteAllBytesAsync(held, Bytes(300_000, 30), TestContext.Current.CancellationToken);
            await AddHeldAsync(database, Video, entryDirectory, held, "2160p");

            var heldPath = EntryPath.At(entryDirectory, ".mkv");
            var relabelled = Path.Combine(entryDirectory, heldPath.VideoFileNameFor("2160p"));
            File.Move(held, relabelled);

            var source = await VideoFileAsync(root, "second.mp4", 300_000, 31);
            var arrivingPath = EntryPath.At(entryDirectory, ".mp4");
            var intended = Path.Combine(entryDirectory, arrivingPath.VideoFileNameFor("1080p"));
            await AddArrivalAsync(
                database,
                Video,
                source,
                "1080p",
                ArrivingFileState.Filing,
                intended);

            await RunAsync(database);

            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var files = await context.VideoFiles.OrderBy(row => row.QualityLabel).ToListAsync(
                TestContext.Current.CancellationToken);
            Assert.Equal(2, files.Count);
            Assert.Contains(files, file => file.FiledPath == relabelled);
            Assert.Contains(files, file => file.FiledPath == intended);
            Assert.Equal(1, await context.OperationLogEntries.CountAsync(
                row => row.Act == "Relabelled",
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recovery_never_recreates_a_recorded_Entry_Directory_that_disappeared()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var entryDirectory = EntryDirectory(root);
            Directory.CreateDirectory(entryDirectory);
            var held = Path.Combine(entryDirectory, "A Site - 2026-08-28 - A Title.mkv");
            await File.WriteAllBytesAsync(held, Bytes(300_000, 32), TestContext.Current.CancellationToken);
            await AddHeldAsync(database, Video, entryDirectory, held, "2160p");
            var source = await VideoFileAsync(root, "waiting.mp4", 300_000, 33);
            var intended = Path.Combine(
                entryDirectory,
                EntryPath.At(entryDirectory, ".mp4").VideoFileNameFor("1080p"));
            await AddArrivalAsync(
                database,
                Video,
                source,
                "1080p",
                ArrivingFileState.Filing,
                intended);
            Directory.Delete(entryDirectory, recursive: true);

            await RunAsync(database);

            await using var scope = database.Scope();
            var arrival = await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(ArrivingFileReason.EntryMissing, arrival.Reason);
            Assert.Equal(ArrivingFileState.AwaitingFiling, arrival.State);
            Assert.Null(arrival.IntendedPath);
            Assert.False(Directory.Exists(entryDirectory));
            Assert.True(File.Exists(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_file_sorts_behind_fresh_work_without_reordering_one_Videos_files()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var database = await PreparedAsync(root);
            var failedSource = await VideoFileAsync(root, "gone.mkv", 300_000, 29);
            var failedIntended = Path.Combine(root, "failed", "gone.mkv");
            await AddArrivalAsync(
                database,
                Video,
                failedSource,
                "1080p",
                ArrivingFileState.Filing,
                failedIntended,
                lastAttempted: database.Time.GetUtcNow());
            File.Delete(failedSource);
            var fresh = await VideoFileAsync(root, "fresh.mkv", 300_000, 28);
            await AddArrivalAsync(database, OtherVideo, fresh, "1080p");

            await RunAsync(database);

            await using var scope = database.Scope();
            var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles
                .ToDictionaryAsync(row => row.SourcePath, TestContext.Current.CancellationToken);
            Assert.Equal(ArrivingFileState.Filing, rows[failedSource].State);
            Assert.Equal(ArrivingFileState.Filed, rows[fresh].State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<TestDatabase> PreparedAsync(string root)
    {
        var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.LibraryRoot, root),
            TestContext.Current.CancellationToken);

        var site = new CatalogueSiteRow { PrdbId = Site, Title = "A Site" };
        context.CatalogueSites.Add(site);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var actor = new CatalogueActorRow { PrdbId = Actor, Name = "An Actor" };
        context.CatalogueActors.Add(actor);
        context.CatalogueVideos.AddRange(
            new CatalogueVideoRow
            {
                PrdbId = Video,
                Title = "A Title",
                SiteId = site.Id,
                ReleaseDate = new DateOnly(2026, 8, 28),
            },
            new CatalogueVideoRow
            {
                PrdbId = OtherVideo,
                Title = "Other Title",
                SiteId = site.Id,
                ReleaseDate = new DateOnly(2026, 8, 29),
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var video = await context.CatalogueVideos.SingleAsync(
            row => row.PrdbId == Video,
            TestContext.Current.CancellationToken);
        context.CatalogueVideoActors.Add(new CatalogueVideoActorRow { VideoId = video.Id, ActorId = actor.Id });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return database;
    }

    private static string EntryDirectory(string root, bool distinguish = false) =>
        EntryPaths.For(
                new FiledVideo(Video, "A Site", "A Title", new DateOnly(2026, 8, 28)),
                ".mkv",
                distinguish)
            .DirectoryUnder(root);

    private static async Task AddArrivalAsync(
        TestDatabase database,
        Guid video,
        string source,
        string quality,
        ArrivingFileState state = ArrivingFileState.AwaitingFiling,
        string? intended = null,
        DateTimeOffset? lastAttempted = null)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        if (!await context.Downloads.AnyAsync(row => row.Id == Download, TestContext.Current.CancellationToken))
        {
            context.Downloads.Add(new DownloadRow
            {
                Id = Download,
                VideoId = Video,
                IndexerId = Indexer,
                DerivedReleaseId = "release",
                SubmittedName = "release",
                State = DownloadState.Collected,
                OutstandingSince = database.Time.GetUtcNow(),
                CreatedAt = database.Time.GetUtcNow(),
            });
        }

        var bytes = await File.ReadAllBytesAsync(
            File.Exists(source) ? source : intended!,
            TestContext.Current.CancellationToken);
        var hashPath = File.Exists(source) ? source : intended!;
        context.ArrivingFiles.Add(new ArrivingFileRow
        {
            Id = Guid.CreateVersion7(database.Time.GetUtcNow()),
            DownloadId = Download,
            IndexerId = Indexer,
            DerivedReleaseId = "release",
            SourcePath = source,
            ArrivedName = Path.GetFileName(source),
            State = state,
            VideoId = video,
            SizeBytes = bytes.LongLength,
            QualityLabel = quality,
            OsHash = OsHash.Compute(hashPath),
            IntendedPath = intended,
            LastAttemptedAt = lastAttempted,
            ProbeOutcome = ProbeOutcome.Read,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AddHeldAsync(
        TestDatabase database,
        Guid video,
        string entryDirectory,
        string path,
        string quality)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = video,
            EntryDirectory = entryDirectory,
            FiledAt = database.Time.GetUtcNow(),
        });
        context.VideoFiles.Add(new VideoFileRow
        {
            Id = Guid.CreateVersion7(database.Time.GetUtcNow()),
            LibraryEntryVideoId = video,
            FiledPath = path,
            QualityLabel = quality,
            SizeBytes = new FileInfo(path).Length,
            OsHash = OsHash.Compute(path),
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<ArrivingFileReason?> ReasonAsync(TestDatabase database, string source)
    {
        await using var scope = database.Scope();
        return (await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles.SingleAsync(
            row => row.SourcePath == source,
            TestContext.Current.CancellationToken)).Reason;
    }

    private static async Task RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<FilingRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static async Task<string> VideoFileAsync(string directory, string name, int size, byte seed)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, Bytes(size, seed), TestContext.Current.CancellationToken);
        return path;
    }

    private static byte[] Bytes(int size, byte seed) =>
        Enumerable.Range(0, size).Select(index => (byte)(seed + index % 233)).ToArray();

    private static async Task<string> WriteComparisonAsync(string root, byte[] bytes)
    {
        var path = Path.Combine(root, "comparison.mkv");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        return path;
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prdb-fab-library", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
