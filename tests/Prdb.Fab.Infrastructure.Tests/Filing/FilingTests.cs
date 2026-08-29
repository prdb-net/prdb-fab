using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Prdb.Hashing;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class FilingTests
{
    private static readonly Guid DownloadId = Guid.Parse("0198ec28-1c00-7000-8000-000000000101");
    private static readonly Guid IndexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000102");
    private static readonly Guid ExactVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000101");
    private static readonly Guid PartialVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000102");
    private static readonly Guid CandidateVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000103");
    private static readonly Guid Site = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000101");

    [Fact]
    public async Task The_probe_reads_the_video_stream_once_and_ignores_attached_artwork()
    {
        var path = Path.Combine(Path.GetTempPath(), $"probe-{Guid.NewGuid():N}.mkv");
        await File.WriteAllBytesAsync(path, new byte[1024], TestContext.Current.CancellationToken);
        try
        {
            var process = new RecordedProbeProcess(ProbeJson());
            var reading = await new VideoProbe(process)
                .ReadAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(ProbeOutcome.Read, reading.Outcome);
            Assert.Equal(63, reading.RuntimeSeconds);
            Assert.Equal(1920, reading.Width);
            Assert.Equal(1080, reading.Height);
            Assert.Equal("h264", reading.VideoCodec);
            Assert.Equal("1080p", reading.QualityLabel);
            Assert.Null(reading.OsHash);
            Assert.Equal([path], process.Paths);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Collecting_handles_file_and_directory_storage_and_never_probes_a_durable_path_twice()
    {
        var root = TemporaryDirectory();
        var single = Path.Combine(root, "single.mkv");
        var nested = Path.Combine(root, "job", "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllBytesAsync(single, new byte[1024], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(nested, "movie.mp4"), new byte[1024], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(nested, "readme.txt"), "not a video", TestContext.Current.CancellationToken);
        var process = new RecordedProbeProcess(ProbeJson());

        try
        {
            await using var database = await TestDatabase.CreateAsync(also: services =>
                services.AddSingleton<IProbeProcess>(process));
            await ConfigureCollectingAsync(database, root);
            await AddDownloadAsync(database, DownloadId, "/sab/single.mkv");
            await AddDownloadAsync(database, Guid.NewGuid(), "/sab/job", "directory");

            await using (var scope = database.Scope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<CollectingRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken);
                Assert.Equal(2, result.ItemsHandled);
            }

            await using (var scope = database.Scope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
                Assert.Equal(2, await context.ArrivingFiles.CountAsync(TestContext.Current.CancellationToken));
                Assert.All(await context.ArrivingFiles.ToListAsync(TestContext.Current.CancellationToken), arrival =>
                {
                    Assert.Equal(ArrivingFileState.AwaitingIdentification, arrival.State);
                    Assert.Null(arrival.Reason);
                    Assert.Equal("1080p", arrival.QualityLabel);
                });
                Assert.Equal(2, await context.Downloads.CountAsync(
                    row => row.State == DownloadState.Collected,
                    TestContext.Current.CancellationToken));

                await context.Downloads.ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.State, DownloadState.Completed),
                    TestContext.Current.CancellationToken);
            }

            await using (var scope = database.Scope())
            {
                await scope.ServiceProvider.GetRequiredService<CollectingRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken);
            }

            Assert.Equal(2, process.Paths.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_repaired_mapping_resumes_the_same_download_and_an_empty_download_is_terminal()
    {
        var root = TemporaryDirectory();
        var job = Path.Combine(root, "job");
        Directory.CreateDirectory(job);
        await File.WriteAllTextAsync(Path.Combine(job, "note.txt"), "empty", TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync(also: services =>
                services.AddSingleton<IProbeProcess>(new RecordedProbeProcess(ProbeJson())));
            await ConfigureCollectingAsync(database, Path.Combine(root, "missing"));
            await AddDownloadAsync(database, DownloadId, "/sab/job");

            await using (var scope = database.Scope())
            {
                var waiting = await scope.ServiceProvider.GetRequiredService<CollectingRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken);
                Assert.Equal(RunOutcome.Succeeded, waiting.Outcome);
                Assert.Equal(0, waiting.ItemsHandled);
            }

            await ConfigureCollectingAsync(database, root);
            await using (var scope = database.Scope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<CollectingRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken);
                Assert.Equal(1, result.ItemsHandled);
            }

            await using var check = database.Scope();
            var download = await check.ServiceProvider.GetRequiredService<FabDbContext>().Downloads
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(DownloadState.Failed, download.State);
            Assert.Equal(DownloadCause.Empty, download.Cause);
            Assert.Equal(DownloadId, download.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Collecting_sets_both_local_reasons_before_any_prdb_work()
    {
        var root = TemporaryDirectory();
        var job = Path.Combine(root, "job");
        Directory.CreateDirectory(job);
        var identical = Path.Combine(job, "identical.mkv");
        var unreadable = Path.Combine(job, "unreadable.mkv");
        await File.WriteAllBytesAsync(identical, new byte[200_000], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(unreadable, new byte[1024], TestContext.Current.CancellationToken);

        try
        {
            await using var database = await TestDatabase.CreateAsync(also: services =>
                services.AddSingleton<IProbeProcess>(new SelectiveProbeProcess()));
            await ConfigureCollectingAsync(database, root);
            await AddDownloadAsync(database, DownloadId, "/sab/job");

            await using (var scope = database.Scope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
                context.LibraryEntries.Add(new LibraryEntryRow
                {
                    VideoId = ExactVideo,
                    EntryDirectory = "/library/exact",
                    FiledAt = database.Time.GetUtcNow(),
                });
                context.VideoFiles.Add(new VideoFileRow
                {
                    Id = Guid.NewGuid(),
                    LibraryEntryVideoId = ExactVideo,
                    FiledPath = "/library/exact/exact.1080p.mkv",
                    QualityLabel = "1080p",
                    SizeBytes = 200_000,
                    OsHash = OsHash.Compute(identical),
                });
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using (var scope = database.Scope())
            {
                await scope.ServiceProvider.GetRequiredService<CollectingRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken);
            }

            await using var check = database.Scope();
            var rows = await check.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles
                .ToDictionaryAsync(row => row.ArrivedName, TestContext.Current.CancellationToken);
            Assert.Equal(ArrivingFileReason.IdenticalFile, rows["identical.mkv"].Reason);
            Assert.Equal(ArrivingFileReason.UnreadableQuality, rows["unreadable.mkv"].Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Arrival_identification_keeps_all_evidence_and_applies_the_named_default_gate()
    {
        var prdb = new FakePrdbApi();
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        prdb.Answers("/videos/identify", IdentifyAnswer(ids));

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PrdbApiKey, "prdb-key"),
                TestContext.Current.CancellationToken);
            context.ArrivingFiles.AddRange(
                Arrival(ids[0], "exact.mkv"),
                Arrival(ids[1], "partial.mkv"),
                Arrival(ids[2], "ambiguous.mkv"),
                Arrival(ids[3], "site.mkv"),
                Arrival(ids[4], "local.mkv", ArrivingFileReason.UnreadableQuality));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ArrivalIdentificationRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(4, result.ItemsHandled);
        }

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var arrivals = await context.ArrivingFiles.ToDictionaryAsync(
                row => row.ArrivedName,
                TestContext.Current.CancellationToken);
            Assert.Equal(ArrivingFileState.AwaitingFiling, arrivals["exact.mkv"].State);
            Assert.Null(arrivals["exact.mkv"].Reason);
            Assert.Equal(IdentificationConfidence.Exact, arrivals["exact.mkv"].Confidence);
            Assert.Equal(IdentificationRung.OsHash, arrivals["exact.mkv"].MatchedBy);
            Assert.Equal(PartialVideo, arrivals["partial.mkv"].VideoId);
            Assert.Equal(ArrivingFileReason.Unidentified, arrivals["partial.mkv"].Reason);
            Assert.Equal(IdentificationConfidence.Partial, arrivals["partial.mkv"].Confidence);
            Assert.Equal(ArrivingFileReason.Unidentified, arrivals["ambiguous.mkv"].Reason);
            Assert.Equal(ArrivingFileReason.Unidentified, arrivals["site.mkv"].Reason);
            Assert.Equal(Site, arrivals["site.mkv"].SiteId);
            Assert.Equal(ArrivingFileReason.UnreadableQuality, arrivals["local.mkv"].Reason);
            Assert.Single(await context.ArrivingFileCandidates.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(4, await context.IdentificationOutcomes.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal(3, await context.CatalogueVideos.CountAsync(TestContext.Current.CancellationToken));

            var pins = scope.ServiceProvider.GetRequiredService<CataloguePins>();
            var exact = await context.CatalogueVideos.SingleAsync(
                row => row.PrdbId == ExactVideo,
                TestContext.Current.CancellationToken);
            var candidate = await context.CatalogueVideos.SingleAsync(
                row => row.PrdbId == CandidateVideo,
                TestContext.Current.CancellationToken);
            Assert.Contains(PinReason.ReviewQueueEntry, await pins.WhyAsync(exact.Id, TestContext.Current.CancellationToken));
            Assert.Contains(PinReason.CandidateVideo, await pins.WhyAsync(candidate.Id, TestContext.Current.CancellationToken));
            Assert.Contains(
                candidate.Id,
                await pins.NewestPinFirst(context.CatalogueVideos)
                    .Select(row => row.Id)
                    .ToListAsync(TestContext.Current.CancellationToken));
        }

        var request = Assert.Single(prdb.AskingFor("/videos/identify"));
        using var body = JsonDocument.Parse(request.Body);
        Assert.True(body.RootElement.GetProperty("includeVideoDetails").GetBoolean());
        var files = body.RootElement.GetProperty("files");
        Assert.Equal(4, files.GetArrayLength());
        Assert.Equal(1024, files[0].GetProperty("filesize").GetInt64());
        Assert.Equal("0123456789ABCDEF", files[0].GetProperty("osHash").GetString());
    }

    [Fact]
    public async Task Changing_the_gate_only_reconsiders_named_answers_still_waiting_on_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        var strong = Guid.NewGuid();
        var exact = Guid.NewGuid();
        var local = Guid.NewGuid();
        var filed = Guid.NewGuid();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.ArrivingFiles.AddRange(
                Arrival(strong, "strong.mkv", state: ArrivingFileState.AwaitingFiling, video: ExactVideo, confidence: IdentificationConfidence.Strong),
                Arrival(exact, "exact.mkv", ArrivingFileReason.Unidentified, video: ExactVideo, confidence: IdentificationConfidence.Exact),
                Arrival(local, "local.mkv", ArrivingFileReason.IdenticalFile, video: ExactVideo, confidence: IdentificationConfidence.Strong),
                Arrival(filed, "filed.mkv", state: ArrivingFileState.Filed, video: ExactVideo, confidence: IdentificationConfidence.Strong));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var scope = database.Scope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<IdentificationSettings>();
            Assert.Equal(AfterDownloadGateChoice.ExactAndStrong, await settings.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, await settings.SaveAsync(AfterDownloadGateChoice.ExactOnly, TestContext.Current.CancellationToken));
        }

        await using (var scope = database.Scope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles
                .ToDictionaryAsync(row => row.ArrivedName, TestContext.Current.CancellationToken);
            Assert.Equal(ArrivingFileReason.Unidentified, rows["strong.mkv"].Reason);
            Assert.Equal(ArrivingFileState.AwaitingFiling, rows["exact.mkv"].State);
            Assert.Equal(ArrivingFileReason.IdenticalFile, rows["local.mkv"].Reason);
            Assert.Equal(ArrivingFileState.Filed, rows["filed.mkv"].State);
        }
    }

    [Fact]
    public async Task A_governor_deferral_leaves_the_arrival_unchanged()
    {
        var prdb = new FakePrdbApi
        {
            Hourly = (Limit: 10, Remaining: 0, ResetInSeconds: 3600),
        };
        prdb.Answers(
            "/user-identity",
            """{"userHash":"5b1f0c2e9a7d4f3b8c6e1a0d2f4b6a8c","activeSubscriptions":[]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        var arrivalId = Guid.NewGuid();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PrdbApiKey, "prdb-key"),
                TestContext.Current.CancellationToken);
            context.ArrivingFiles.Add(Arrival(arrivalId, "deferred.mkv"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            await scope.ServiceProvider.GetRequiredService<PrdbGateway>()
                .CheckAsync("prdb-key", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<PrdbDeferredException>(() =>
                scope.ServiceProvider.GetRequiredService<ArrivalIdentificationRoutine>()
                    .RunAsync(null, TestContext.Current.CancellationToken));
        }

        await using var check = database.Scope();
        var unchanged = await check.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ArrivingFileState.AwaitingIdentification, unchanged.State);
        Assert.Null(unchanged.Reason);
        Assert.Null(unchanged.VideoId);
        Assert.Empty(prdb.AskingFor("/videos/identify"));
    }

    [Fact]
    public async Task All_four_filing_routines_with_executable_code_are_registered()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var scope = database.Scope();
        var names = scope.ServiceProvider.GetServices<IRoutine>().Select(routine => routine.Name).ToArray();
        Assert.Contains(CollectingRoutine.RoutineName, names);
        Assert.Contains(ArrivalIdentificationRoutine.RoutineName, names);
        Assert.Contains(FilingRoutine.RoutineName, names);
        Assert.Contains(TidyUpRoutine.RoutineName, names);
    }

    private static ArrivingFileRow Arrival(
        Guid id,
        string name,
        ArrivingFileReason? reason = null,
        ArrivingFileState state = ArrivingFileState.AwaitingIdentification,
        Guid? video = null,
        IdentificationConfidence? confidence = null) => new()
    {
        Id = id,
        DownloadId = DownloadId,
        IndexerId = IndexerId,
        DerivedReleaseId = "release",
        SourcePath = "/local/" + name,
        ArrivedName = name,
        State = state,
        Reason = reason,
        VideoId = video,
        Confidence = confidence,
        SizeBytes = 1024,
        QualityLabel = "1080p",
        OsHash = "0123456789abcdef",
        ProbeOutcome = ProbeOutcome.Read,
    };

    private static async Task ConfigureCollectingAsync(TestDatabase database, string localRoot)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Installation.ExecuteUpdateAsync(
            update => update
                .SetProperty(row => row.PathMappingFrom, "/sab")
                .SetProperty(row => row.PathMappingTo, localRoot),
            TestContext.Current.CancellationToken);
    }

    private static async Task AddDownloadAsync(
        TestDatabase database,
        Guid id,
        string storage,
        string release = "single")
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.Downloads.Add(new DownloadRow
        {
            Id = id,
            VideoId = Guid.NewGuid(),
            IndexerId = IndexerId,
            DerivedReleaseId = release,
            SubmittedName = release,
            State = DownloadState.Completed,
            Storage = storage,
            OutstandingSince = database.Time.GetUtcNow(),
            CreatedAt = database.Time.GetUtcNow(),
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prdb-fab-filing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ProbeJson() => """
        {
          "streams": [
            { "width": 1000, "height": 1000, "codec_name": "mjpeg", "disposition": { "attached_pic": 1 } },
            { "width": 1920, "height": 1080, "codec_name": "h264", "disposition": { "attached_pic": 0 } }
          ],
          "format": { "duration": "62.6" }
        }
        """;

    private static string IdentifyAnswer(IReadOnlyList<Guid> ids) => $$"""
        {
          "results": [
            {
              "ref": "{{ids[0]:D}}", "videoId": "{{ExactVideo}}", "confidence": 4, "matchedBy": 0,
              "candidates": [],
              "video": { "id": "{{ExactVideo}}", "title": "Exact", "preNames": [], "actors": [], "images": [] }
            },
            {
              "ref": "{{ids[1]:D}}", "videoId": "{{PartialVideo}}", "confidence": 1, "matchedBy": 2,
              "candidates": [],
              "video": { "id": "{{PartialVideo}}", "title": "Partial", "preNames": [], "actors": [], "images": [] }
            },
            {
              "ref": "{{ids[2]:D}}", "confidence": 5, "candidates": ["{{CandidateVideo}}"]
            },
            {
              "ref": "{{ids[3]:D}}", "confidence": 1, "matchedBy": 4, "candidates": [],
              "site": { "id": "{{Site}}", "title": "A Site" }
            }
          ]
        }
        """;

    private sealed class RecordedProbeProcess(string json) : IProbeProcess
    {
        public List<string> Paths { get; } = [];

        public Task<ProbeProcessResult> RunAsync(string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            return Task.FromResult(new ProbeProcessResult(0, json, string.Empty, false));
        }
    }

    private sealed class SelectiveProbeProcess : IProbeProcess
    {
        public Task<ProbeProcessResult> RunAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(Path.GetFileName(path).StartsWith("unreadable", StringComparison.Ordinal)
                ? new ProbeProcessResult(1, string.Empty, "not a video", false)
                : new ProbeProcessResult(0, ProbeJson(), string.Empty, false));
    }
}
