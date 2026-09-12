using System.Net;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Backup;

/// <summary>
/// What a Restore hands to the machinery that was already running: outstanding
/// Downloads that SABnzbd may or may not still know about, and a Library the
/// tool has not looked at yet.
/// </summary>
public sealed class RestoreResumptionTests : IDisposable
{
    private readonly string library = NewDirectory();
    private readonly string downloads = NewDirectory();

    public void Dispose()
    {
        Remove(library);
        Remove(downloads);
    }

    /// <summary>
    /// ADR 0009: a Download still unfinished at export time is looked up at
    /// SABnzbd by its job id and followed again where it still knows it.
    /// </summary>
    /// <remarks>
    /// Nothing new was written for this, which is the finding worth recording:
    /// the follow routine's work set is "Outstanding", the restored rows are
    /// Outstanding, and the job id came back in the file. What this asserts is
    /// that the seam actually meets — a restored row is not distinguishable
    /// from one this installation submitted itself.
    /// </remarks>
    [Fact]
    public async Task A_restored_outstanding_download_is_followed_by_its_job_id()
    {
        var sabnzbd = new SabnzbdStandIn
        {
            History = """
                {"history":{"slots":[
                  {"nzo_id":"nzo-restored","name":"A.Release.1080p","status":"Completed",
                   "fail_message":"","stage_log":[],"storage":"/remote/complete/A.Release.1080p"}
                ]}}
                """,
        };

        await using var target = await RestoredAsync(Outstanding(await AnExportAsync()), sabnzbd);
        await using var scope = target.Scope();

        await scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        var download = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Downloads.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DownloadState.Completed, download.State);
        Assert.Equal("nzo-restored", sabnzbd.LastQueueIds);
    }

    /// <summary>
    /// ADR 0009: where SABnzbd no longer knows the job, the Download failed and
    /// the Release is consumed for that Video — <em>that is what CONTEXT.md
    /// already means by consumed, not an exception to it</em>. So the settled
    /// rule applies, absences and all, rather than a Restore-shaped shortcut.
    /// </summary>
    [Fact]
    public async Task A_job_sabnzbd_no_longer_knows_ends_consumed_by_the_settled_rule()
    {
        await using var target = await RestoredAsync(Outstanding(await AnExportAsync()), new SabnzbdStandIn());
        await using var scope = target.Scope();
        var routine = scope.ServiceProvider.GetRequiredService<DownloadFollowingRoutine>();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        for (var absence = 1; absence < DownloadFollowing.AbsencesBeforeVanished; absence++)
        {
            await routine.RunAsync(null, TestContext.Current.CancellationToken);

            var waiting = await context.Downloads.AsNoTracking()
                .SingleAsync(TestContext.Current.CancellationToken);

            Assert.Equal(DownloadState.Outstanding, waiting.State);
            Assert.Equal(absence, waiting.ConsecutiveAbsences);
        }

        await routine.RunAsync(null, TestContext.Current.CancellationToken);

        var download = await context.Downloads.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DownloadState.Failed, download.State);
        Assert.Equal(DownloadCause.Vanished, download.Cause);

        // Consumed: the Release the Download names still has its row, so
        // ADR 0008's ranking will not offer it to this Video again.
        Assert.Equal("release-1", download.DerivedReleaseId);
    }

    /// <summary>
    /// ADR 0009: <em>a restore that hashes a whole library before it finishes
    /// is a restore people interrupt</em>. So a Restore writes the rows and
    /// leaves the whole Library as the background pass's work set.
    /// </summary>
    [Fact]
    public async Task A_restore_hashes_nothing_and_leaves_the_library_as_the_work_set()
    {
        await using var target = await RestoredAsync(await AnExportAsync(), new SabnzbdStandIn());
        await using var scope = target.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Empty(await context.LibraryVerifications.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.VideoFiles.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The pass, over a Library whose files are actually there, moved and all.
    /// </summary>
    [Fact]
    public async Task The_pass_confirms_a_library_that_moved_with_its_installation()
    {
        var document = await AnExportAsync();
        var hash = await AFileOnDiskAsync("A Site/An Entry/video.mkv");
        var withHash = document with
        {
            VideoFiles = [document.VideoFiles[0] with { OsHash = hash }],
        };

        await using var target = await RestoredAsync(withHash, new SabnzbdStandIn());
        await using var scope = target.Scope();

        var run = await scope.ServiceProvider.GetRequiredService<LibraryVerificationRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(1, run.ItemsHandled);

        var answer = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .LibraryVerifications.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LibraryVerification.Confirmed, answer.Outcome);
    }

    /// <summary>
    /// The three ways it does not confirm, and the one thing all of them have
    /// in common: the Library Entry is still there afterwards.
    /// </summary>
    [Theory]
    [InlineData(false, false, LibraryVerification.Missing)]
    [InlineData(true, false, LibraryVerification.Mismatched)]
    [InlineData(true, true, LibraryVerification.Confirmed)]
    public async Task What_the_pass_finds_never_costs_the_entry(
        bool present,
        bool matching,
        LibraryVerification expected)
    {
        var document = await AnExportAsync();
        var hash = present
            ? await AFileOnDiskAsync("A Site/An Entry/video.mkv")
            : "0123456789abcdef";
        var recorded = matching ? hash : "ffffffffffffffff";
        var withHash = document with
        {
            VideoFiles = [document.VideoFiles[0] with { OsHash = recorded }],
        };

        await using var target = await RestoredAsync(withHash, new SabnzbdStandIn());
        await using var scope = target.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        await scope.ServiceProvider.GetRequiredService<LibraryVerificationRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(
            expected,
            (await context.LibraryVerifications.SingleAsync(TestContext.Current.CancellationToken)).Outcome);

        // ADR 0009: nothing is deleted and nothing is re-fetched on the
        // strength of a missing file.
        Assert.Equal(1, await context.LibraryEntries.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.VideoFiles.CountAsync(TestContext.Current.CancellationToken));

        // And no remote Fulfilment is disturbed: the reported state is the row
        // that would have to change for prdb to hear about this, and it does
        // not.
        var reported = await context.ReportedStates.SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(reported.IsFulfilled);
        Assert.Null(reported.TerminalOutcome);
    }

    /// <summary>
    /// ADR 0009: <em>until an entry is verified it counts as held</em> — which
    /// under ADR 0007 is what stops a mis-mounted library reading as a standing
    /// instruction to download the collection again.
    /// </summary>
    /// <remarks>
    /// Held is read off the existence of the Library Entry rather than off a
    /// flag, so what makes this true is that the pass leaves the row alone. The
    /// test drives the real eligibility check rather than asserting the row is
    /// still there, because the row being there is only interesting for what it
    /// causes.
    /// </remarks>
    [Fact]
    public async Task Automation_will_not_replace_content_the_pass_could_not_confirm()
    {
        await using var target = await RestoredAsync(await AnExportAsync(), new SabnzbdStandIn());
        await using var scope = target.Scope();

        // The file is not on disk here at all, so the pass answers Missing.
        await scope.ServiceProvider.GetRequiredService<LibraryVerificationRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(
            LibraryVerification.Missing,
            (await context.LibraryVerifications.SingleAsync(TestContext.Current.CancellationToken)).Outcome);

        var eligibility = await scope.ServiceProvider.GetRequiredService<AutomaticEligibility>()
            .ForVideoAsync(
                APopulatedInstallation.Video,
                [
                    new ReleaseChoice(
                        Id: 1,
                        IndexerId: APopulatedInstallation.Indexer,
                        IndexerName: "An Indexer",
                        DerivedReleaseId: "release-2",
                        Title: "A.Better.Release.2160p",
                        Size: 1_500,
                        Confidence: IdentificationConfidence.Exact,
                        Position: 1,
                        Exclusion: null),
                ],
                TestContext.Current.CancellationToken);

        var verdict = Assert.Single(eligibility).Value;

        Assert.False(verdict.Eligible);

        // NotWanted, because a restored installation has no Catalogue yet and
        // therefore no wanted list — which is itself a refusal. What must never
        // appear is a verdict that permits the download.
        Assert.True(verdict.Reason is AutomationDecisionReason.NotWanted or AutomationDecisionReason.HeldVideo);
    }

    private static BackupDocument Outstanding(BackupDocument document) => document with
    {
        Downloads =
        [
            document.Downloads[0] with
            {
                State = DownloadState.Outstanding,
                NzoId = "nzo-restored",
                ConsecutiveAbsences = 0,
            },
        ],
    };

    private static async Task<BackupDocument> AnExportAsync()
    {
        await using var source = await TestDatabase.CreateAsync();
        await APopulatedInstallation.SeedAsync(source);
        await using var scope = source.Scope();

        return await scope.ServiceProvider.GetRequiredService<Backups>()
            .ReadAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A real file under the answered Library root, and its osHash.</summary>
    private async Task<string> AFileOnDiskAsync(string relative)
    {
        var path = Path.Combine(library, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Larger than the two 64 KiB windows osHash reads, so the hash is of a
        // file rather than of a special case.
        await File.WriteAllBytesAsync(
            path,
            [.. Enumerable.Range(0, 200_000).Select(at => (byte)(at % 251))],
            TestContext.Current.CancellationToken);

        if (!Prdb.Hashing.OsHash.TryCompute(path, out var computed) || computed is null)
        {
            throw new InvalidOperationException("The fixture file could not be hashed.");
        }

        return computed;
    }

    private async Task<TestDatabase> RestoredAsync(BackupDocument document, SabnzbdStandIn sabnzbd)
    {
        var target = await TestDatabase.CreateAsync(also: services =>
            services.AddHttpClient(FabTransports.Sabnzbd).ConfigurePrimaryHttpMessageHandler(() => sabnzbd));

        await using (var scope = target.Scope())
        {
            var act = await scope.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
                BackupJson.Write(document),
                new RestoreRoots(library, downloads),
                TestContext.Current.CancellationToken);

            Assert.Equal(RestoreOutcome.Restored, act.Outcome);
        }

        return target;
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "prdb-fab-resume", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(path);

        return path;
    }

    private static void Remove(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    /// <summary>
    /// ADR 0042: the network is replaced at the socket, so the gateway, the
    /// timeout and the parsing are all the real thing.
    /// </summary>
    private sealed class SabnzbdStandIn : HttpMessageHandler
    {
        public string Queue { get; init; } = """{"queue":{"paused":false,"slots":[]}}""";

        public string History { get; init; } = """{"history":{"slots":[]}}""";

        public string LastQueueIds { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri?.Query ?? string.Empty);

            return Task.FromResult((query["mode"] ?? string.Empty) switch
            {
                "queue" => Answer(Queue, query["nzo_ids"]),
                "history" => Answer(History, null),
                "get_cats" => Answer("""{"categories":["xxx"]}""", null),
                _ => Answer("{}", null),
            });
        }

        private HttpResponseMessage Answer(string body, string? queueIds)
        {
            if (queueIds is not null) LastQueueIds = queueIds;

            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
