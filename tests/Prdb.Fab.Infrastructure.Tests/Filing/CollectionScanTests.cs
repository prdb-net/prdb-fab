using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Tests.Sync;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class CollectionScanTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fab-collection-" + Guid.NewGuid().ToString("N"));
    private string Library => Path.Combine(root, "library");
    private string Existing => Path.Combine(root, "existing");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public CollectionScanTests()
    {
        Directory.CreateDirectory(Library);
        Directory.CreateDirectory(Existing);
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task Empty_storage_completes_without_an_approval_or_a_download()
    {
        await using var database = await TestDatabase.CreateAsync();
        await StartAsync(database);
        await RunAsync(database);
        await RunAsync(database);
        await RunAsync(database);
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal("Ready", (await context.CollectionPreparations.SingleAsync(Token)).Phase);
        Assert.Empty(await context.ArrivingFiles.ToListAsync(Token));
        Assert.Empty(await context.Downloads.ToListAsync(Token));
    }

    [Fact]
    public async Task Enumeration_is_bounded_and_a_changed_directory_restarts_without_duplicate_files()
    {
        for (var i = 0; i < 205; i++) await File.WriteAllTextAsync(Path.Combine(Existing, $"b{i:000}.mkv"), "fixture", Token);
        var probe = new Probe();
        await using var database = await TestDatabase.CreateAsync(also: services => services.AddSingleton<IProbeProcess>(probe));
        await StartAsync(database);
        await RunAsync(database);
        await using (var scope = database.Scope())
            Assert.Equal(200, await scope.ServiceProvider.GetRequiredService<FabDbContext>().CollectionFiles.CountAsync(Token));
        await File.WriteAllTextAsync(Path.Combine(Existing, "a.mkv"), "inserted behind cursor", Token);
        Directory.SetLastWriteTimeUtc(Existing, Directory.GetLastWriteTimeUtc(Existing).AddSeconds(2));
        for (var i = 0; i < 4; i++) await RunAsync(database);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            Assert.Equal(206, await context.CollectionFiles.CountAsync(Token));
            Assert.InRange(await context.ArrivingFiles.CountAsync(Token), 1, CollectionScanRoutine.ProbesPerTurn);
        }
        var before = probe.Read;
        await RunAsync(database);
        Assert.Equal(before + CollectionScanRoutine.ProbesPerTurn, probe.Read);
        Assert.Equal(206, Directory.GetFiles(Existing).Length);
    }

    [Fact]
    public async Task Missing_mount_is_a_failed_preparation_never_empty_storage()
    {
        await using var database = await TestDatabase.CreateAsync();
        await StartAsync(database);
        Directory.Delete(Existing);
        var result = await RunAsync(database);
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        await using var scope = database.Scope();
        var row = await scope.ServiceProvider.GetRequiredService<FabDbContext>().CollectionPreparations.SingleAsync(Token);
        Assert.NotEqual("Ready", row.Phase);
        Assert.NotNull(row.Error);
    }

    [Fact]
    public async Task Symlinks_downloads_and_recorded_library_files_are_not_imported()
    {
        var nested = Path.Combine(Existing, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "keep.mkv"), "video", Token);
        Directory.CreateSymbolicLink(Path.Combine(nested, "cycle"), Existing);
        Directory.CreateSymbolicLink(Path.Combine(Existing, "escape"), root);
        var downloads = Path.Combine(Existing, "downloads");
        Directory.CreateDirectory(downloads);
        await File.WriteAllTextAsync(Path.Combine(downloads, "skip.mkv"), "download", Token);
        var held = Path.Combine(Library, "held.mkv");
        await File.WriteAllTextAsync(held, "held", Token);
        var probe = new Probe();
        await using var database = await TestDatabase.CreateAsync(also: services => services.AddSingleton<IProbeProcess>(probe));
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var video = Guid.NewGuid();
            context.LibraryEntries.Add(new() { VideoId = video, EntryDirectory = Library });
            context.VideoFiles.Add(new() { Id = Guid.NewGuid(), LibraryEntryVideoId = video, FiledPath = held, QualityLabel = "1080p" });
            await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.PathMappingTo, downloads), Token);
            await context.SaveChangesAsync(Token);
        }
        await StartAsync(database, [Existing, nested]);
        for (var i = 0; i < 5; i++) await RunAsync(database);
        await using var checking = database.Scope();
        var db = checking.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Single(await db.CollectionFiles.ToListAsync(Token));
        Assert.Equal(1, probe.Read);
        Assert.Empty(await db.Downloads.ToListAsync(Token));
        Assert.True(File.Exists(held));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(0)]
    public async Task Identification_is_shared_but_does_not_authorize_filing_and_scan_blocks_automation(int confidence)
    {
        await File.WriteAllBytesAsync(Path.Combine(Existing, "first.mkv"), new byte[200_000], Token);
        var api = new FakePrdbApi();
        var probe = new Probe();
        await using var database = await TestDatabase.CreateAsync(prdb: api, also: services => services.AddSingleton<IProbeProcess>(probe));
        await StartAsync(database);
        for (var i = 0; i < 3; i++) await RunAsync(database);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var arrival = await context.ArrivingFiles.SingleAsync(Token);
            Assert.Null(arrival.DownloadId);
            Assert.Null(arrival.IndexerId);
            Assert.NotNull(arrival.OsHash);
            var video = Guid.NewGuid();
            api.Answers("/videos/identify", System.Text.Json.JsonSerializer.Serialize(new
            {
                results = new[] { new { @ref = arrival.Id.ToString("D"), videoId = confidence == 4 ? (Guid?)video : null, confidence, matchedBy = 0,
                    candidates = confidence == 5 ? new[] { video } : Array.Empty<Guid>(),
                    video = confidence == 4 ? new { id = video, title = "Existing video", site = new { id = Guid.NewGuid(), title = "Site" } } : null } },
            }));
            await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.PrdbApiKey, "fixture"), Token);
            await scope.ServiceProvider.GetRequiredService<ArrivalIdentificationRoutine>().RunAsync(null, Token);
        }
        await RunAsync(database);
        await using var read = database.Scope();
        var held = read.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal("Planning", (await held.CollectionPreparations.SingleAsync(Token)).Phase);
        Assert.Equal(confidence == 4 ? ArrivingFileState.AwaitingFiling : ArrivingFileState.AwaitingIdentification,
            (await held.ArrivingFiles.SingleAsync(Token)).State);
        Assert.Equal(confidence == 5 ? 1 : 0, await held.ArrivingFileCandidates.CountAsync(Token));
        Assert.Equal(RunResult.NothingToDo, await read.ServiceProvider.GetRequiredService<FilingRoutine>().RunAsync(null, Token));
        var choice = new ReleaseChoice(1, Guid.NewGuid(), "Indexer", "release", "release", 100, IdentificationConfidence.Exact, 1, null);
        var result = await read.ServiceProvider.GetRequiredService<AutomaticEligibility>().ForVideoAsync(Guid.NewGuid(), [choice], Token);
        Assert.True(result[1].Wait);
        Assert.Equal(AutomationDecisionReason.CollectionPreparation, result[1].Reason);
        Assert.True(File.Exists(Path.Combine(Existing, "first.mkv")));
    }

    [Fact]
    public async Task A_disappeared_file_can_be_explicitly_left_but_its_directory_must_still_be_readable()
    {
        var path = Path.Combine(Existing, "vanished.mkv");
        await File.WriteAllTextAsync(path, "fixture", Token);
        await using var database = await TestDatabase.CreateAsync();
        await StartAsync(database);
        await RunAsync(database);
        File.Delete(path);
        for (var i = 0; i < 6; i++) await RunAsync(database);
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await scope.ServiceProvider.GetRequiredService<CollectionPlanningRoutine>().RunAsync(null, Token);
        var preview = scope.ServiceProvider.GetRequiredService<CollectionPreview>();
        var page = await preview.ReadAsync(1, Token);
        Assert.Equal("Preview", page.Phase);
        Assert.Equal(1, page.Unresolved);
        Assert.Equal(ProbeOutcome.SourceMissing, (await context.ArrivingFiles.SingleAsync(Token)).ProbeOutcome);
        Assert.True((await preview.ApproveAsync(page.Revision, [], [], false, true, Token)).Accepted);
        await scope.ServiceProvider.GetRequiredService<CollectionImportRoutine>().RunAsync(null, Token);
        Assert.Equal("Ready", (await context.CollectionPreparations.SingleAsync(Token)).Phase);
    }

    [Fact]
    public async Task Explicit_recheck_reprobes_unchanged_stamps_after_an_unreadable_file_is_repaired()
    {
        var path = Path.Combine(Existing, "repaired.mkv");
        await File.WriteAllTextAsync(path, "fixture", Token);
        var probe = new Probe();
        await using var database = await TestDatabase.CreateAsync(also: services => services.AddSingleton<IProbeProcess>(probe));
        await StartAsync(database);
        for (var i = 0; i < 3; i++) await RunAsync(database);
        Assert.Equal(1, probe.Read);
        await using (var scope = database.Scope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<CollectionPreparation>().RetryAsync(Token)).Accepted);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>().ArrivingFiles.ToListAsync(Token));
        }
        for (var i = 0; i < 3; i++) await RunAsync(database);
        Assert.Equal(2, probe.Read);
    }

    private async Task StartAsync(TestDatabase database, string[]? directories = null)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        await context.Installation.ExecuteUpdateAsync(update => update.SetProperty(row => row.LibraryRoot, Library), Token);
        var result = await scope.ServiceProvider.GetRequiredService<CollectionPreparation>().StartAsync(directories ?? [Existing], Token);
        Assert.True(result.Accepted, result.Detail);
    }

    private static async Task<RunResult> RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        return await scope.ServiceProvider.GetRequiredService<CollectionScanRoutine>().RunAsync(null, Token);
    }

    private sealed class Probe : IProbeProcess
    {
        public int Read { get; private set; }
        public Task<ProbeProcessResult> RunAsync(string path, CancellationToken cancellationToken)
        {
            Read++;
            return Task.FromResult(new ProbeProcessResult(0,
                """{"streams":[{"width":1920,"height":1080,"codec_name":"h264"}],"format":{"duration":"60"}}""", "", false));
        }
    }
}
