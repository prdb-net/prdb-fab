using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Tests.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class ReviewAndTidyTests
{
    [Fact]
    public async Task Contact_sheet_reads_only_an_unchanged_open_file()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "review.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);
        var rendered = new byte[] { 7, 8, 9 };
        var process = new RecordedContactSheetProcess(rendered);

        try
        {
            await using var database = await TestDatabase.CreateAsync(
                also: services => services.AddSingleton<IContactSheetProcess>(process));
            var download = Guid.NewGuid();
            var arrival = Arrival(Guid.NewGuid(), download, path, 4);
            arrival.RuntimeSeconds = 1_653;
            await SeedDownloadAndArrivalsAsync(database, download, arrival);

            await using var scope = database.Scope();
            var sheet = await scope.ServiceProvider.GetRequiredService<ReviewFileContactSheet>()
                .ReadAsync(arrival.Id, TestContext.Current.CancellationToken);

            Assert.Equal(rendered, sheet);
            Assert.Equal(path, process.Path);
            Assert.Equal(1_653, process.RuntimeSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Contact_sheet_refuses_a_file_that_changed_after_collecting()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "changed.mp4");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4], TestContext.Current.CancellationToken);
        var process = new RecordedContactSheetProcess([7, 8, 9]);

        try
        {
            await using var database = await TestDatabase.CreateAsync(
                also: services => services.AddSingleton<IContactSheetProcess>(process));
            var download = Guid.NewGuid();
            var arrival = Arrival(Guid.NewGuid(), download, path, 3);
            arrival.RuntimeSeconds = 60;
            await SeedDownloadAndArrivalsAsync(database, download, arrival);

            await using var scope = database.Scope();
            var sheet = await scope.ServiceProvider.GetRequiredService<ReviewFileContactSheet>()
                .ReadAsync(arrival.Id, TestContext.Current.CancellationToken);

            Assert.Null(sheet);
            Assert.Null(process.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Review_queue_exposes_probe_facts_and_known_video_artwork()
    {
        await using var database = await TestDatabase.CreateAsync();
        var download = Guid.NewGuid();
        var videoId = Guid.NewGuid();
        var arrival = Arrival(Guid.NewGuid(), download, "/downloads/review.mkv", 16L * 1024 * 1024 * 1024);
        arrival.VideoId = videoId;
        arrival.RuntimeSeconds = 1_653;
        arrival.QualityLabel = "2160p";
        arrival.Width = 3_840;
        arrival.Height = 2_160;
        arrival.VideoCodec = "h264";
        await SeedDownloadAndArrivalsAsync(database, download, arrival);

        long artworkId;
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var video = new CatalogueVideoRow
            {
                PrdbId = videoId,
                Title = "Known Video",
                DurationMs = 1_650_000,
                DurationFileCount = 4,
            };
            context.CatalogueVideos.Add(video);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            artworkId = video.Id;
        }

        await using var read = database.Scope();
        var page = await read.ServiceProvider.GetRequiredService<ReviewQueue>()
            .ReadAsync(null, null, 1, TestContext.Current.CancellationToken);
        var entry = Assert.Single(page.Entries);
        Assert.Equal(1_653, entry.RuntimeSeconds);
        Assert.Equal("2160p", entry.Quality);
        Assert.Equal(3_840, entry.Width);
        Assert.Equal(2_160, entry.Height);
        Assert.Equal("h264", entry.VideoCodec);
        Assert.NotNull(entry.Video);
        Assert.Equal(artworkId, entry.Video.ArtworkId);
    }

    [Fact]
    public async Task Live_review_search_links_only_known_catalogue_artwork()
    {
        var knownId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var prdb = new FakePrdbApi().Answers(
            "/videos",
            $$"""
            {
              "items": [
                { "id": "{{knownId:D}}", "title": "Known", "siteTitle": "Site", "releaseDate": "2026-01-02", "durationMs": 123000, "durationFileCount": 3 },
                { "id": "{{unknownId:D}}", "title": "Unknown", "siteTitle": "Site", "releaseDate": "2026-01-01" }
              ],
              "page": 1,
              "pageSize": 20,
              "totalCount": 2
            }
            """);
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        long artworkId;
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PrdbApiKey, "prdb-key"),
                TestContext.Current.CancellationToken);
            var known = new CatalogueVideoRow { PrdbId = knownId, Title = "Known" };
            context.CatalogueVideos.Add(known);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            artworkId = known.Id;
        }

        await using var search = database.Scope();
        var page = await search.ServiceProvider.GetRequiredService<ReviewVideoSearch>()
            .SearchAsync("known", null, 1, TestContext.Current.CancellationToken);
        Assert.Equal(2, page.Total);
        Assert.Equal(artworkId, page.Videos.Single(video => video.Id == knownId).ArtworkId);
        Assert.Null(page.Videos.Single(video => video.Id == unknownId).ArtworkId);
    }

    [Fact]
    public async Task File_As_records_a_confirmed_assignment_before_returning_to_filing()
    {
        var chosen = Guid.NewGuid();
        var prdb = new FakePrdbApi().Answers(
            $"/videos/{chosen:D}",
            $$"""{"id":"{{chosen:D}}","title":"Chosen Video","images":[],"actors":[],"preNames":[]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        var download = Guid.NewGuid();
        var arrival = Arrival(Guid.NewGuid(), download, "/downloads/chosen.mkv", 17);
        arrival.OsHash = "0123456789abcdef";
        await SeedDownloadAndArrivalsAsync(database, download, arrival);
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.PrdbApiKey, "prdb-key")
                    .SetProperty(row => row.PrdbUserHash, "person-hash"),
                TestContext.Current.CancellationToken);
            var verdict = await scope.ServiceProvider.GetRequiredService<ReviewDecisions>()
                .FileAsAsync(arrival.Id, chosen, TestContext.Current.CancellationToken);
            Assert.Equal(ReviewDecisionOutcome.QueuedForFiling, verdict.Outcome);
        }

        await using var check = database.Scope();
        var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
        var assignment = await context.ConfirmedAssignments.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(chosen, assignment.VideoId);
        Assert.Equal("person-hash", assignment.UserHash);
        var queued = await context.ArrivingFiles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(queued.Reason);
        Assert.Equal(ArrivingFileState.AwaitingFiling, queued.State);
    }

    [Fact]
    public async Task Delete_confirms_exact_files_records_each_act_and_dismiss_leaves_disk_untouched()
    {
        var directory = TemporaryDirectory();
        var deleted = Path.Combine(directory, "deleted.mkv");
        var dismissed = Path.Combine(directory, "dismissed.mkv");
        await File.WriteAllBytesAsync(deleted, new byte[17], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(dismissed, new byte[23], TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var download = Guid.NewGuid();
            var deleteId = Guid.NewGuid();
            var dismissId = Guid.NewGuid();
            await SeedDownloadAndArrivalsAsync(database, download,
                Arrival(deleteId, download, deleted, 17),
                Arrival(dismissId, download, dismissed, 23));

            await using (var scope = database.Scope())
            {
                var queue = scope.ServiceProvider.GetRequiredService<ReviewQueue>();
                var removed = await queue.DeleteAsync([deleteId], TestContext.Current.CancellationToken);
                var left = await queue.DismissAsync([dismissId], TestContext.Current.CancellationToken);
                Assert.Equal(ReviewSelectionOutcome.Deleted, removed.Outcome);
                Assert.Equal(ReviewSelectionOutcome.Dismissed, left.Outcome);
            }

            Assert.False(File.Exists(deleted));
            Assert.True(File.Exists(dismissed));
            await using var check = database.Scope();
            var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Empty(await context.ArrivingFiles.ToListAsync(TestContext.Current.CancellationToken));
            var operation = await context.OperationLogEntries.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Deleted", operation.Act);
            Assert.Equal(deleted, operation.PathBefore);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_refuses_a_file_whose_confirmed_size_changed()
    {
        var directory = TemporaryDirectory();
        var path = Path.Combine(directory, "changed.mkv");
        await File.WriteAllBytesAsync(path, new byte[18], TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var download = Guid.NewGuid();
            var id = Guid.NewGuid();
            await SeedDownloadAndArrivalsAsync(database, download, Arrival(id, download, path, 17));

            await using var scope = database.Scope();
            var verdict = await scope.ServiceProvider.GetRequiredService<ReviewQueue>()
                .DeleteAsync([id], TestContext.Current.CancellationToken);
            Assert.Equal(ReviewSelectionOutcome.SelectionChanged, verdict.Outcome);
            Assert.True(File.Exists(path));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .OperationLogEntries.ToListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tidy_removes_only_fixed_leftovers_after_every_review_row_has_exited()
    {
        var root = TemporaryDirectory();
        var job = Path.Combine(root, "job");
        Directory.CreateDirectory(job);
        var leftover = Path.Combine(job, "sample.nfo");
        var unsupported = Path.Combine(job, "keep.dat");
        await File.WriteAllTextAsync(leftover, "remove", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(unsupported, "keep", TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var download = Guid.NewGuid();
            await SeedDownloadAndArrivalsAsync(
                database,
                download,
                Arrival(Guid.NewGuid(), download, Path.Combine(job, "review.mkv"), 1));
            await ConfigureTidyAsync(database, root);

            await using (var scope = database.Scope())
            {
                var routine = scope.ServiceProvider.GetRequiredService<TidyUpRoutine>();
                Assert.Equal(0, (await routine.RunAsync(null, TestContext.Current.CancellationToken)).ItemsHandled);
                await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles.ExecuteDeleteAsync(
                    TestContext.Current.CancellationToken);
            }

            await using (var scope = database.Scope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<TidyUpRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken);
                Assert.Equal(1, result.ItemsHandled);
            }

            Assert.False(File.Exists(leftover));
            Assert.True(File.Exists(unsupported));
            await using var check = database.Scope();
            var context = check.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.NotNull((await context.Downloads.SingleAsync(TestContext.Current.CancellationToken)).TidiedAt);
            Assert.Equal("Tidied", (await context.OperationLogEntries.SingleAsync(TestContext.Current.CancellationToken)).Act);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Tidy_never_uses_the_parent_of_single_file_storage()
    {
        var root = TemporaryDirectory();
        var storage = Path.Combine(root, "video.mkv");
        var neighbour = Path.Combine(root, "neighbour.nfo");
        await File.WriteAllTextAsync(storage, "video", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(neighbour, "keep", TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            await SeedDownloadAndArrivalsAsync(database, Guid.NewGuid());
            await ConfigureTidyAsync(database, root, "/sab/video.mkv");
            await using var scope = database.Scope();
            await scope.ServiceProvider.GetRequiredService<TidyUpRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(neighbour));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Replace_is_durable_then_runs_once_in_the_serial_file_lane()
    {
        var root = TemporaryDirectory();
        var entryDirectory = Path.Combine(root, "Entry");
        Directory.CreateDirectory(entryDirectory);
        var filedPath = Path.Combine(entryDirectory, "Entry.mkv");
        var sourcePath = Path.Combine(root, "replacement.mkv");
        await File.WriteAllBytesAsync(filedPath, [1, 1, 1, 1], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(sourcePath, [2, 2, 2, 2], TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync();
            var download = Guid.NewGuid();
            var video = Guid.NewGuid();
            var arrival = Arrival(Guid.NewGuid(), download, sourcePath, 4);
            arrival.Reason = ArrivingFileReason.Duplicate;
            arrival.VideoId = video;
            arrival.QualityLabel = "1080p";
            await SeedDownloadAndArrivalsAsync(database, download, arrival);
            await using (var scope = database.Scope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
                context.LibraryEntries.Add(new LibraryEntryRow
                {
                    VideoId = video,
                    EntryDirectory = entryDirectory,
                    FiledAt = database.Time.GetUtcNow(),
                });
                context.VideoFiles.Add(new VideoFileRow
                {
                    Id = Guid.NewGuid(),
                    LibraryEntryVideoId = video,
                    FiledPath = filedPath,
                    QualityLabel = "1080p",
                    SizeBytes = 4,
                });
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);

                var queued = await scope.ServiceProvider.GetRequiredService<ReviewDecisions>()
                    .ReplaceAsync(arrival.Id, TestContext.Current.CancellationToken);
                Assert.Equal(ReviewDecisionOutcome.QueuedForReplacement, queued.Outcome);
                Assert.True(File.Exists(sourcePath));
            }

            await using (var scope = database.Scope())
            {
                var routine = scope.ServiceProvider.GetRequiredService<FilingRoutine>();
                Assert.Equal(1, (await routine.RunAsync(null, TestContext.Current.CancellationToken)).ItemsHandled);
                Assert.Same(RunResult.NothingToDo, await routine.RunAsync(null, TestContext.Current.CancellationToken));
            }

            Assert.Equal([2, 2, 2, 2], await File.ReadAllBytesAsync(filedPath, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(sourcePath));
            await using var check = database.Scope();
            var checkedContext = check.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Empty(await checkedContext.ArrivingFiles.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Replaced", (await checkedContext.OperationLogEntries.SingleAsync(TestContext.Current.CancellationToken)).Act);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ArrivingFileRow Arrival(Guid id, Guid download, string path, long size) => new()
    {
        Id = id,
        DownloadId = download,
        IndexerId = Guid.NewGuid(),
        DerivedReleaseId = "release",
        SourcePath = path,
        ArrivedName = Path.GetFileName(path),
        IsOnDisk = true,
        State = ArrivingFileState.AwaitingIdentification,
        Reason = ArrivingFileReason.Unidentified,
        SizeBytes = size,
        ProbeOutcome = ProbeOutcome.Read,
    };

    private static async Task SeedDownloadAndArrivalsAsync(
        TestDatabase database,
        Guid download,
        params ArrivingFileRow[] arrivals)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Downloads.Add(new DownloadRow
        {
            Id = download,
            VideoId = Guid.NewGuid(),
            IndexerId = Guid.NewGuid(),
            DerivedReleaseId = "release",
            SubmittedName = "Download",
            State = DownloadState.Collected,
            Storage = "/sab/job",
            OutstandingSince = database.Time.GetUtcNow(),
            CreatedAt = database.Time.GetUtcNow(),
        });
        context.ArrivingFiles.AddRange(arrivals);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ConfigureTidyAsync(
        TestDatabase database,
        string root,
        string storage = "/sab/job")
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(
            update => update
                .SetProperty(row => row.PathMappingFrom, "/sab")
                .SetProperty(row => row.PathMappingTo, root)
                .SetProperty(row => row.DeleteLeftovers, true),
            TestContext.Current.CancellationToken);
        await context.Downloads.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.Storage, storage),
            TestContext.Current.CancellationToken);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prdb-fab-review", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordedContactSheetProcess(byte[] bytes) : IContactSheetProcess
    {
        public string? Path { get; private set; }
        public long? RuntimeSeconds { get; private set; }

        public Task<ContactSheetProcessResult> RunAsync(
            string path,
            long runtimeSeconds,
            CancellationToken cancellationToken)
        {
            Path = path;
            RuntimeSeconds = runtimeSeconds;
            return Task.FromResult(new ContactSheetProcessResult(0, bytes, false));
        }
    }
}
