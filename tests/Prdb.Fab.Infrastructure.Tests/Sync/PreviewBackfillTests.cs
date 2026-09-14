using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Backup;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Hashing;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0064's explicit bounded request: what selects a file the Library already
/// held, what never does, and what a pause or a cancellation reaches.
/// </summary>
/// <remarks>
/// <para>
/// The one thing standing in for something real is the ffmpeg subprocess, as in
/// <see cref="PreviewGenerationTests"/> and for the same reason: it is a
/// process rather than a collaborator. Everything else — the eligibility query,
/// the request, the order the routine takes its work in, the store — is the
/// code that ships.
/// </para>
/// <para>
/// The case worth reading is
/// <see cref="A_newly_filed_file_is_generated_before_a_request_s_backlog"/>.
/// ADR 0064's automatic scope is the file that has just been filed, and a
/// person who files one while a Library of five thousand is draining must not
/// wait a week for the picture of the file they were actually watching.
/// </para>
/// </remarks>
public sealed class PreviewBackfillTests
{
    private const string UserHash = "a-user-hash";
    private const long Runtime = 1200;

    /// <summary>
    /// Nothing starts a backfill. An installation full of eligible files that
    /// nobody has asked about publishes none of them, however many runs go by.
    /// </summary>
    [Fact]
    public async Task The_existing_library_is_published_by_nothing_but_a_request()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 3);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        await using var scope = database.Scope();

        Assert.Equal(
            0,
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .PreviewPublications.CountAsync(TestContext.Current.CancellationToken));

        // And the offer says how many there would be, which is what a person is
        // shown before the button.
        var offer = await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
            .OfferAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, offer.Eligible);
        Assert.True(offer.Askable);
    }

    /// <summary>
    /// The request takes one file per run and stops of its own accord when the
    /// Library has none left.
    /// </summary>
    [Fact]
    public async Task A_request_takes_up_one_file_a_run_and_finishes_when_there_are_none()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 2);
        await AskAsync(database);


        await RunAsync(database);
        await RunAsync(database);

        var rows = await RowsAsync(database);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(PreviewPublicationState.Ready, row.State));
        Assert.All(rows, row => Assert.NotNull(row.BackfillId));

        // The run after the last one finds nothing left, which is the only way
        // a request ends on its own.
        await RunAsync(database);

        var request = await RequestAsync(database, rows[0].BackfillId!.Value);

        Assert.Equal(PreviewBackfillState.Finished, request.State);
        Assert.Equal(2, request.TakenUp);
        Assert.NotNull(request.SettledAt);

        // A finished request asks for nothing more, and the routine goes quiet.
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
    }

    /// <summary>
    /// ADR 0064's automatic scope stays in front of a backlog somebody asked
    /// for, whatever the clock says about which was written first.
    /// </summary>
    [Fact]
    public async Task A_newly_filed_file_is_generated_before_a_request_s_backlog()
    {
        await using var database = await CreateAsync();
        var library = await ALibraryAsync(database, files: 2);
        await AskAsync(database);

        // The request takes one up, so its intent is the older of the two.
        await RunAsync(database);

        database.Time.Advance(TimeSpan.FromMinutes(10));

        var filed = await FileAsync(database, "a file that just arrived.mkv");

        await RunAsync(database);

        var rows = await RowsAsync(database);
        var ours = Assert.Single(rows, row => row.BackfillId is null);

        Assert.Equal(PreviewPublicationState.Ready, ours.State);
        Assert.Equal(filed, ours.VideoFileId);

        // And exactly one of the request's own is generated, which is the one
        // the run before this took up.
        Assert.Equal(
            1,
            rows.Count(row => row.BackfillId is not null
                              && row.State == PreviewPublicationState.Ready));

        Assert.Equal(2, library.Count);
    }

    /// <summary>
    /// Pausing holds the work back and gives nothing up, which is what makes
    /// resuming mean anything.
    /// </summary>
    [Fact]
    public async Task Pausing_holds_the_work_and_resuming_lets_it_go_on()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 3);
        var id = await AskAsync(database);

        await RunAsync(database);

        await using (var scope = database.Scope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
                .PauseAsync(id, TestContext.Current.CancellationToken));
        }

        // Nothing is taken up and nothing more is generated while it is held.
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        var held = await RowsAsync(database);

        Assert.Single(held);
        Assert.Equal(PreviewPublicationState.Ready, held[0].State);
        Assert.True(Store(database).Holds(held[0].Id), "a paused request gave up its bytes.");

        await using (var scope = database.Scope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
                .ResumeAsync(id, TestContext.Current.CancellationToken));
        }

        await RunAsync(database);

        Assert.Equal(2, (await RowsAsync(database)).Count);
    }

    /// <summary>
    /// Cancelling reaches what has not left, and nothing else.
    /// </summary>
    /// <remarks>
    /// The two rows that stay are the point of the test. A submission prdb
    /// accepted stays accepted — this tool has no retraction — and an upload
    /// whose outcome nobody could establish stays the question it is, because
    /// cancelling a request is not an answer to one.
    /// </remarks>
    [Fact]
    public async Task Cancelling_gives_up_what_has_not_left_and_nothing_that_has()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 2);
        var id = await AskAsync(database);

        await RunAsync(database);
        await RunAsync(database);

        var generated = await RowsAsync(database);

        Assert.Equal(2, generated.Count);

        // One of them has been sent and one is still queued, which is the shape
        // a cancellation in the middle of a drain finds.
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var sent = await context.PreviewPublications
                .AsTracking()
                .FirstAsync(row => row.Id == generated[0].Id, TestContext.Current.CancellationToken);

            sent.State = PreviewPublicationState.Sent;
            sent.PrdbImageId = Guid.NewGuid();
            sent.SettledAt = database.Time.GetUtcNow();

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        PreviewBackfillCancellation? cancelled;

        await using (var scope = database.Scope())
        {
            cancelled = await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
                .CancelAsync(id, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(cancelled);
        Assert.Equal(1, cancelled.Dropped);
        Assert.Equal(1, cancelled.Accepted);

        var after = await RowsAsync(database);

        Assert.Equal(PreviewPublicationState.Sent, after.Single(row => row.Id == generated[0].Id).State);
        Assert.Equal(PreviewPublicationState.Dropped, after.Single(row => row.Id == generated[1].Id).State);

        Assert.False(Store(database).Holds(generated[1].Id), "a cancelled publication kept its bytes.");

        var request = await RequestAsync(database, id);

        Assert.Equal(PreviewBackfillState.Cancelled, request.State);
        Assert.Contains("stay at prdb", request.Note!, StringComparison.Ordinal);

        // And the routine has nothing left to do: a cancelled request takes
        // nothing up.
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
    }

    /// <summary>
    /// A second request finds only what the first did not reach, which is what
    /// keeps a Library from being published twice.
    /// </summary>
    /// <remarks>
    /// Nothing remembers which files the first request looked at. What is left
    /// to take up is a query over the publication rows, so a file that was
    /// submitted, declined or given up on answers the question as finally as
    /// one that is still waiting.
    /// </remarks>
    [Fact]
    public async Task A_second_request_offers_only_what_the_first_did_not_reach()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 3);
        var first = await AskAsync(database);

        await RunAsync(database);
        await RunAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
                .CancelAsync(first, TestContext.Current.CancellationToken);
        }

        Guid? second;

        await using (var scope = database.Scope())
        {
            var backfill = scope.ServiceProvider.GetRequiredService<PreviewBackfill>();

            // The two the first request reached are answered for — both of them
            // by having been given up — so only the third is offered.
            var offer = await backfill.OfferAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, offer.Eligible);

            second = (await backfill.AskAsync(TestContext.Current.CancellationToken)).Id;

            Assert.NotNull(second);
        }

        await RunAsync(database);
        await RunAsync(database);

        // Three rows for three files, never four, and the second request took
        // up exactly one.
        Assert.Equal(3, (await RowsAsync(database)).Count);
        Assert.Equal(1, (await RequestAsync(database, second.Value)).TakenUp);
    }

    /// <summary>
    /// One request at a time: a second alongside the first would select the
    /// same files, and the two would disagree about what cancelling means.
    /// </summary>
    [Fact]
    public async Task A_second_request_is_refused_while_one_is_under_way()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 2);
        await AskAsync(database);

        await using var scope = database.Scope();

        var verdict = await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
            .AskAsync(TestContext.Current.CancellationToken);

        Assert.Null(verdict.Id);
        Assert.Contains("already under way", verdict.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The switch and the explanation gate a request exactly as they gate a
    /// filed file, and neither refusal is a failure.
    /// </summary>
    [Theory]
    [InlineData(false, true, "switched off")]
    [InlineData(true, false, "has not been read")]
    public async Task A_request_is_refused_where_publishing_would_send_nothing(
        bool publishing,
        bool explained,
        string says)
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 2, publishing: publishing, explained: explained);

        await using var scope = database.Scope();
        var backfill = scope.ServiceProvider.GetRequiredService<PreviewBackfill>();

        var offer = await backfill.OfferAsync(TestContext.Current.CancellationToken);

        Assert.False(offer.Askable);

        var verdict = await backfill.AskAsync(TestContext.Current.CancellationToken);

        Assert.Null(verdict.Id);
        Assert.Contains(says, verdict.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A request is a decision to publish this Library under one account, and
    /// the next account never took it.
    /// </summary>
    [Fact]
    public async Task A_request_does_not_survive_the_account_that_made_it()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 3);
        var id = await AskAsync(database);

        await RunAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Installation
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.PrdbUserHash, "another-user-hash"),
                    TestContext.Current.CancellationToken);
        }

        await RunAsync(database);

        var request = await RequestAsync(database, id);

        Assert.Equal(PreviewBackfillState.Cancelled, request.State);
        Assert.Contains("account changed", request.Note!, StringComparison.Ordinal);

        // And the request takes nothing else up under the new key. What is
        // already generated is given up by the delivering routine's own account
        // pass, which is where ADR 0064's rule about a pending upload lives.
        Assert.Single(await RowsAsync(database));
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
    }

    /// <summary>
    /// A file with no osHash or no Runtime is not eligible, so a request never
    /// offers it and never stalls on it.
    /// </summary>
    [Fact]
    public async Task A_file_a_preview_cannot_be_made_from_is_not_eligible()
    {
        await using var database = await CreateAsync();
        await ALibraryAsync(database, files: 1);

        await using (var writing = database.Scope())
        {
            var context = writing.ServiceProvider.GetRequiredService<FabDbContext>();

            // One with no Runtime and one with no hash, beside the one good
            // file: neither can produce the picture ADR 0064 publishes.
            foreach (var (name, osHash, runtime) in ((string, string?, long?)[])
                     [("no runtime.mkv", "0F0E0D0C0B0A0908", null), ("no hash.mkv", null, Runtime)])
            {
                var video = Guid.NewGuid();

                context.LibraryEntries.Add(new LibraryEntryRow
                {
                    VideoId = video,
                    EntryDirectory = "/library",
                    FiledAt = database.Time.GetUtcNow(),
                });

                context.VideoFiles.Add(AFile(Guid.NewGuid(), $"/library/{name}", osHash, runtime, video));
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = database.Scope();

        Assert.Equal(
            1,
            (await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
                .OfferAsync(TestContext.Current.CancellationToken)).Eligible);
    }

    /// <summary>
    /// A Restore does not resume a request, and does not republish what the
    /// document says was already sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0064 names startup, upgrading and enabling the switch as events that
    /// must not start a backfill. Restoring is the same kind of event and the
    /// most dangerous of them, because the Library a document is restored onto
    /// may not be the Library it was written from — so the request deliberately
    /// stays behind at the export boundary and somebody asks again, knowing
    /// what they are asking for.
    /// </para>
    /// <para>
    /// <strong>What does cross is the history</strong>, which is the half that
    /// prevents duplicates: a file whose publication row came back with the
    /// document is answered for, so a fresh request offers only the rest.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_restore_resumes_no_request_and_republishes_nothing()
    {
        await using var source = await CreateAsync();
        await ALibraryAsync(source, files: 3);
        await AskAsync(source);

        await RunAsync(source);

        // One of the three left the machine, which is the fact a Restore has to
        // carry: it cannot be fetched again while moderation keeps it invisible.
        await using (var scope = source.Scope())
        {
            var writing = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var sent = await writing.PreviewPublications
                .AsTracking()
                .SingleAsync(TestContext.Current.CancellationToken);

            sent.State = PreviewPublicationState.Sent;
            sent.PrdbImageId = Guid.NewGuid();
            sent.SettledAt = source.Time.GetUtcNow();

            await writing.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        string document;

        await using (var scope = source.Scope())
        {
            document = BackupJson.Write(
                await scope.ServiceProvider.GetRequiredService<Backups>()
                    .ReadAsync(TestContext.Current.CancellationToken));
        }

        await using var target = await CreateAsync();
        await using var restoring = target.Scope();

        var library = Path.Combine(target.Location.DataDirectory, "library");
        var downloads = Path.Combine(target.Location.DataDirectory, "downloads");

        Directory.CreateDirectory(library);
        Directory.CreateDirectory(downloads);

        var act = await restoring.ServiceProvider.GetRequiredService<Restores>().ApplyAsync(
            document,
            new RestoreRoots(library, downloads),
            TestContext.Current.CancellationToken);

        Assert.Equal(RestoreOutcome.Restored, act.Outcome);

        var context = restoring.ServiceProvider.GetRequiredService<FabDbContext>();

        // No request came over, so nothing is taken up and the routine is idle.
        Assert.Equal(0, await context.PreviewBackfills.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RunResult.NothingToDo, await RunAsync(target));

        // The submission did come over, and it is what keeps the file out of
        // the next request. Its note about which request selected it did not:
        // a restored row is one Filing could have written, which is what it is
        // once the request is gone.
        var carried = Assert.Single(
            await context.PreviewPublications.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PreviewPublicationState.Sent, carried.State);
        Assert.Null(carried.BackfillId);

        Assert.Equal(
            2,
            (await restoring.ServiceProvider.GetRequiredService<PreviewBackfill>()
                .OfferAsync(TestContext.Current.CancellationToken)).Eligible);
    }

    private static async Task<RunResult> RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PreviewGenerationRoutine>()
            .RunAsync(target: null, TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> AskAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        var verdict = await scope.ServiceProvider.GetRequiredService<PreviewBackfill>()
            .AskAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(verdict.Id);

        return verdict.Id.Value;
    }

    private static async Task<IReadOnlyList<PreviewPublicationRow>> RowsAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .PreviewPublications.AsNoTracking()
            .OrderBy(row => row.IntendedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One request by name, because the clock does not move in a test and two
    /// of them written at the same instant cannot be told apart by it.
    /// </summary>
    private static async Task<PreviewBackfillRow> RequestAsync(TestDatabase database, Guid id)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .PreviewBackfills.AsNoTracking()
            .SingleAsync(row => row.Id == id, TestContext.Current.CancellationToken);
    }

    private static PublicationStore Store(TestDatabase database) =>
        database.Services.CreateScope().ServiceProvider.GetRequiredService<PublicationStore>();

    /// <summary>
    /// A Library of filed, identified Video Files on disk, and an installation
    /// that has been told what publishing sends.
    /// </summary>
    /// <remarks>
    /// Nothing here writes a publication row: this is deliberately the state an
    /// installation is in after upgrading into ADR 0064, where the whole
    /// Library is eligible and none of it is owed anything.
    /// </remarks>
    private static async Task<IReadOnlyList<Guid>> ALibraryAsync(
        TestDatabase database,
        int files,
        bool publishing = true,
        bool explained = true)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var installation = await context.Installation
            .AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        installation.PrdbUserHash = UserHash;
        installation.PrdbApiKey = "0123456789abcdef0123456789abcdef";
        installation.PublishGeneratedPreviews = publishing;
        installation.PreviewPublicationExplainedAt = explained ? database.Time.GetUtcNow() : null;

        var directory = Path.Combine(database.Location.DataDirectory, "library");

        Directory.CreateDirectory(directory);

        var written = new List<Guid>();

        for (var index = 0; index < files; index++)
        {
            var video = Guid.NewGuid();
            var path = Path.Combine(directory, $"scene {index}.mkv");

            await System.IO.File.WriteAllBytesAsync(
                path,
                Bytes(200_000, (byte)(index + 1)),
                TestContext.Current.CancellationToken);

            context.LibraryEntries.Add(new LibraryEntryRow
            {
                VideoId = video,
                EntryDirectory = directory,
                FiledAt = database.Time.GetUtcNow(),
            });

            var id = Guid.NewGuid();

            context.VideoFiles.Add(AFile(id, path, OsHash.Compute(path), Runtime, video));

            written.Add(id);
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return written;
    }

    /// <summary>
    /// One more filed, identified Video File, owed a preview the way Filing
    /// writes one.
    /// </summary>
    private static async Task<Guid> FileAsync(TestDatabase database, string name)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var directory = Path.Combine(database.Location.DataDirectory, "library");
        var path = Path.Combine(directory, name);
        var video = Guid.NewGuid();
        var id = Guid.NewGuid();

        await System.IO.File.WriteAllBytesAsync(
            path,
            Bytes(200_000, fill: 99),
            TestContext.Current.CancellationToken);

        var hash = OsHash.Compute(path);

        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = video,
            EntryDirectory = directory,
            FiledAt = database.Time.GetUtcNow(),
        });

        context.VideoFiles.Add(AFile(id, path, hash, Runtime, video));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Filing's own act, unchanged: an intent with no request on it.
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<PreviewPublications>()
            .IntendAsync(id, video, hash, Runtime, TestContext.Current.CancellationToken));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return id;
    }

    private static VideoFileRow AFile(
        Guid id,
        string path,
        string? osHash,
        long? runtime,
        Guid? video = null) => new()
    {
        Id = id,
        LibraryEntryVideoId = video ?? Guid.NewGuid(),
        FiledPath = path,
        QualityLabel = "1080p",
        SizeBytes = System.IO.File.Exists(path) ? new FileInfo(path).Length : 200_000,
        RuntimeSeconds = runtime,
        Width = 1920,
        Height = 1080,
        VideoCodec = "h264",
        OsHash = osHash,
    };

    private static byte[] Bytes(int length, byte fill)
    {
        var bytes = new byte[length];

        Array.Fill(bytes, fill);

        return bytes;
    }

    /// <summary>
    /// A JPEG that is nothing but a frame header of whatever geometry the plan
    /// asks for, which is all the validator reads.
    /// </summary>
    private static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x11, 0x08,
        (byte)(height >> 8), (byte)(height & 0xFF),
        (byte)(width >> 8), (byte)(width & 0xFF),
        0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xD9,
    ];

    private static Task<TestDatabase> CreateAsync()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(Runtime));

        return TestDatabase.CreateAsync(also: services =>
        {
            services.AddFabSync();
            services.AddSingleton<ISpriteSheetProcess>(
                new SheetOfThePlan(Jpeg(plan.Width, plan.Height)));
        });
    }

    /// <summary>
    /// Stands where ffmpeg stands and nowhere else: handed a path and a plan,
    /// it answers with a sheet of the geometry that was asked for.
    /// </summary>
    private sealed class SheetOfThePlan(byte[] sheet) : ISpriteSheetProcess
    {
        public Task<FfmpegCaptureResult> RunAsync(
            string path,
            SpriteSheetPlan plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FfmpegCaptureResult(
                0,
                sheet,
                TimedOut: false,
                TooLarge: false,
                Error: string.Empty));
    }
}
