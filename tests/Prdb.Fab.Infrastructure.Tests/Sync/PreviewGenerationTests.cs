using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Hashing;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0064's generation: what becomes a pair waiting to be sent, what is given
/// up on before anything is decoded, and what the run leaves behind.
/// </summary>
/// <remarks>
/// ADR 0035's rule holds: the only thing standing in for something real is the
/// ffmpeg subprocess, which is a process rather than a collaborator, and it
/// stands in exactly where <c>IContactSheetProcess</c> already does. Everything
/// above it — the eligibility, the contract, the validator, the store, the row
/// — is the code that ships.
/// </remarks>
public sealed class PreviewGenerationTests
{
    private static readonly Guid Video = Guid.Parse("cccc1111-0000-4000-8000-000000000001");
    private static readonly Guid File = Guid.Parse("dddd1111-0000-4000-8000-000000000001");

    private const string UserHash = "a-user-hash";
    private const long Runtime = 1200;

    /// <summary>
    /// The whole path: an owed file, a decode, a sheet whose geometry is what
    /// was asked for, a WebVTT that reads back against it, both halves on disk,
    /// and only then a row that says Ready.
    /// </summary>
    [Fact]
    public async Task A_pair_is_committed_once_the_sheet_and_its_cues_hold_together()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(Runtime));
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(plan.Width, plan.Height));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        var result = await RunAsync(database);

        Assert.Equal(RunOutcome.Succeeded, result.Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Ready, row.State);
        Assert.Equal(plan.Tiles, row.Tiles);
        Assert.Equal(plan.Columns, row.Columns);
        Assert.Equal(plan.Rows, row.Rows);
        Assert.NotNull(row.GeneratedAt);
        Assert.Null(row.Note);

        Assert.True(Store(database).Holds(row.Id), "the generated pair is not on disk.");

        // And the WebVTT on disk is the one the shared validator accepts
        // against the sheet beside it.
        var vtt = await System.IO.File.ReadAllBytesAsync(
            Store(database).VttPathOf(row.Id),
            TestContext.Current.CancellationToken);

        var timeline = SpriteTimeline.Read(vtt, (plan.Width, plan.Height), PreviewPublicationContract.MostTiles);

        Assert.True(timeline.Usable, timeline.Reason);
        Assert.Equal(plan.Tiles, timeline.Tiles.Count);

        // The file itself was never asked for as anything but a path, and what
        // ffmpeg was asked for is the plan's own arithmetic.
        Assert.Equal(1, ffmpeg.Runs);
        Assert.Equal(plan.Tiles, ffmpeg.Plan!.Tiles);
    }

    /// <summary>
    /// A sheet that is not the grid that was asked for is refused rather than
    /// published. Its cues would point at the wrong seconds and nothing on
    /// screen would say so.
    /// </summary>
    [Fact]
    public async Task A_sheet_of_the_wrong_geometry_is_not_published()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1280, 720));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        var result = await RunAsync(database);

        Assert.Equal(RunOutcome.Failed, result.Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Intended, row.State);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("1280x720", row.Note!, StringComparison.Ordinal);
        Assert.False(Store(database).Holds(row.Id), "a refused pair left bytes on disk.");
    }

    /// <summary>
    /// A decode that never works is given up on rather than retried until the
    /// intent expires a month later — which, at a twenty-minute timeout and a
    /// five-minute cadence, is most of a month of decoding.
    /// </summary>
    [Fact]
    public async Task A_decode_that_never_works_is_given_up_on()
    {
        var ffmpeg = new FakeSpriteSheetProcess(new FfmpegCaptureResult(
            1,
            [],
            TimedOut: false,
            TooLarge: false,
            Error: "moov atom not found"));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        for (var attempt = 0; attempt < PreviewPublicationContract.MostAttempts; attempt++)
        {
            Assert.Equal(RunOutcome.Failed, (await RunAsync(database)).Outcome);
        }

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Dropped, row.State);
        Assert.Equal(PreviewPublicationContract.MostAttempts, row.Attempts);
        Assert.Contains("moov atom not found", row.Note!, StringComparison.Ordinal);
        Assert.NotNull(row.SettledAt);

        // And it is not picked up again on the next pass.
        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
        Assert.Equal(PreviewPublicationContract.MostAttempts, ffmpeg.Runs);
    }

    /// <summary>
    /// Eligibility is a claim about one osHash. A file whose bytes are no
    /// longer those bytes is dropped before anything is decoded, so no picture
    /// of one file is ever published under the hash of another.
    /// </summary>
    [Fact]
    public async Task A_replaced_file_is_dropped_before_it_is_decoded()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        var path = await GiveAsync(database);

        await System.IO.File.WriteAllBytesAsync(
            path,
            Bytes(200_000, fill: 7),
            TestContext.Current.CancellationToken);

        var result = await RunAsync(database);

        Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        Assert.Contains("not the ones the preview was intended for", result.Reason!, StringComparison.Ordinal);

        Assert.Equal(PreviewPublicationState.Dropped, (await RowAsync(database)).State);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// A file that left the Library is the Library's business and is already
    /// reported there. It is dropped rather than raised again here.
    /// </summary>
    [Fact]
    public async Task A_file_that_left_the_library_is_dropped()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .VideoFiles
                .Where(row => row.Id == File)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await RunAsync(database);

        Assert.Equal(PreviewPublicationState.Dropped, (await RowAsync(database)).State);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// ADR 0064's gate: the switch says yes and nobody has read what would
    /// leave, so nothing is decoded and nothing is sent. A Brake rather than a
    /// Gap — the intent stays exactly where it was.
    /// </summary>
    [Fact]
    public async Task Nothing_is_generated_before_the_explanation()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database, explained: false);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        Assert.Equal(PreviewPublicationState.Intended, (await RowAsync(database)).State);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// The switch off stops generation and leaves what is owed alone: turning
    /// it off retracts nothing and expires nothing, and what was waiting is
    /// still waiting.
    /// </summary>
    [Fact]
    public async Task Nothing_is_generated_while_the_switch_is_off()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Installation
                .ExecuteUpdateAsync(
                    row => row.SetProperty(installation => installation.PublishGeneratedPreviews, false),
                    TestContext.Current.CancellationToken);
        }

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        Assert.Equal(PreviewPublicationState.Intended, (await RowAsync(database)).State);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// prdb's population is short of sheets; this file's is already there. The
    /// decode is skipped and the reason is recorded, because a person reading
    /// the surface is owed the difference between <em>nothing happened</em> and
    /// <em>nothing needed to</em>.
    /// </summary>
    [Fact]
    public async Task A_sheet_prdb_already_shows_of_this_file_is_not_made_again()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        var path = await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.UserPreviews.Add(new UserPreviewRow
            {
                PrdbId = Guid.NewGuid(),
                VideoPrdbId = Video,
                OsHash = UserPreviewHash.Normalise(OsHash.Compute(path))!,
                Kind = UserPreviewAsset.SpriteSheet,
                Url = "https://cdn.example/somebody-elses.jpg",
                VttUrl = "https://cdn.example/somebody-elses.vtt",
                TileCount = 120,
                Shown = true,
                UpdatedAtUtc = database.Time.GetUtcNow(),
                CreatedAtUtc = database.Time.GetUtcNow(),
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(database);

        Assert.Equal(RunOutcome.Succeeded, result.Outcome);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Dropped, row.State);
        Assert.Contains("already shows", row.Note!, StringComparison.Ordinal);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// The transient store is bounded by a count, and the stop is a Brake: the
    /// backlog of pairs waiting for their own uploads drains before another is
    /// made.
    /// </summary>
    [Fact]
    public async Task Generation_stops_while_the_contract_s_pairs_are_waiting()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            for (var index = 0; index < PreviewPublicationContract.MostWaiting; index++)
            {
                context.PreviewPublications.Add(new PreviewPublicationRow
                {
                    Id = Guid.NewGuid(),
                    VideoFileId = Guid.NewGuid(),
                    VideoPrdbId = Guid.NewGuid(),
                    OsHash = $"{index:X16}",
                    UserHash = UserHash,
                    OutputVersion = PreviewPublicationContract.OutputVersion,
                    State = PreviewPublicationState.Ready,
                    IntendedAt = database.Time.GetUtcNow(),
                    GeneratedAt = database.Time.GetUtcNow(),
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));

        Assert.Equal(PreviewPublicationState.Intended, (await RowAsync(database)).State);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// Changing the prdb key does not carry pending uploads over to the new
    /// account. Sending one account's queue under another's key is the failure
    /// this prevents.
    /// </summary>
    [Fact]
    public async Task An_intent_made_under_another_account_is_dropped()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Installation
                .ExecuteUpdateAsync(
                    row => row.SetProperty(installation => installation.PrdbUserHash, "somebody-else"),
                    TestContext.Current.CancellationToken);
        }

        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Dropped, row.State);
        Assert.Contains("account changed", row.Note!, StringComparison.Ordinal);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// A month-old unsent intent belongs to an installation that was switched
    /// off, and the file it describes has had a month to become a different
    /// file.
    /// </summary>
    [Fact]
    public async Task An_intent_nothing_touched_for_a_month_is_dropped()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        database.Time.Advance(PreviewPublicationContract.IntentExpiry + TimeSpan.FromDays(1));

        await RunAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(PreviewPublicationState.Dropped, row.State);
        Assert.Contains("expired", row.Note!, StringComparison.Ordinal);
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// What a decode that did not finish leaves behind is taken back: the part
    /// file of a write interrupted by a restart, and the halves of a pair whose
    /// row no longer claims them.
    /// </summary>
    [Fact]
    public async Task The_bytes_of_a_decode_that_did_not_finish_are_reclaimed()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(Runtime));
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(plan.Width, plan.Height));

        await using var database = await CreateAsync(ffmpeg);
        await GiveAsync(database);

        var store = Store(database);
        var orphan = Guid.NewGuid();

        await store.WritePairAsync(orphan, [1, 2, 3], [4, 5, 6], TestContext.Current.CancellationToken);

        var part = store.SheetPathOf(Guid.NewGuid()) + ".half" + PublicationFile.PartSuffix;

        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        await System.IO.File.WriteAllBytesAsync(part, [7], TestContext.Current.CancellationToken);

        await RunAsync(database);

        Assert.False(store.Holds(orphan), "a pair no row claims was left on disk.");
        Assert.False(System.IO.File.Exists(part), "a half-written file was left on disk.");

        // And the pair that was generated in the same run is still there: the
        // sweep reads the rows rather than the clock.
        Assert.True(store.Holds((await RowAsync(database)).Id), "the run's own pair was swept.");
    }

    /// <summary>
    /// The one thing worth checking about the ffmpeg command without a decoder:
    /// that the sampling and the grid are the plan's, and that the offset is a
    /// seek rather than a guess.
    /// </summary>
    [Fact]
    public void The_decode_asks_for_the_plan_s_own_sampling_and_grid()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(Runtime));
        var arguments = FfmpegSpriteSheetProcess.Arguments("/library/a file.mkv", plan);
        var filter = arguments[arguments.ToList().IndexOf("-vf") + 1];

        // The interval as a ratio of two integers rather than a decimal, so the
        // last frame lands where its cue says it does.
        Assert.Contains($"fps={plan.Tiles}/{Runtime}", filter, StringComparison.Ordinal);
        Assert.Contains($"tile={plan.Columns}x{plan.Rows}", filter, StringComparison.Ordinal);
        Assert.Contains("crop=320:180", filter, StringComparison.Ordinal);

        // And no seek: the fps filter already lands half an interval in, so a
        // seek would move every tile a further half interval on.
        Assert.DoesNotContain("-ss", arguments);

        // Nothing but the picture: no audio, no subtitles, no data, and the
        // video stream named rather than chosen.
        Assert.Contains("-an", arguments);
        Assert.Contains("0:v:0", arguments);

        // The path is an argument of its own, so nothing in it can be read as
        // an option.
        Assert.Contains("/library/a file.mkv", arguments);
    }

    /// <summary>
    /// An installation with nothing owed spends nothing on this routine, which
    /// is what <c>IWorkSetPaced</c> means.
    /// </summary>
    [Fact]
    public async Task An_installation_with_nothing_owed_does_no_work()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);

        Assert.Equal(RunResult.NothingToDo, await RunAsync(database));
        Assert.Equal(0, ffmpeg.Runs);
    }

    /// <summary>
    /// The same bytes are one publication however many Library entries hold
    /// them, which is what the table's key says and what keeps a second filing
    /// from making a second picture of one file.
    /// </summary>
    [Fact]
    public async Task The_same_bytes_are_owed_once()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        var path = await GiveAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var publications = scope.ServiceProvider.GetRequiredService<PreviewPublications>();

        var again = await publications.IntendAsync(
            Guid.NewGuid(),
            Video,
            OsHash.Compute(path),
            Runtime,
            TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(again);
        Assert.Equal(1, await context.PreviewPublications.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A file with no measured Runtime is not eligible: no interval can be
    /// computed for it, and a single frame from an unknown position is a weaker
    /// picture than none.
    /// </summary>
    [Fact]
    public async Task A_file_with_no_runtime_is_not_owed_a_preview()
    {
        var ffmpeg = new FakeSpriteSheetProcess(Jpeg(1600, 900));

        await using var database = await CreateAsync(ffmpeg);
        var path = await GiveAsync(database, owed: false);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var owed = await scope.ServiceProvider.GetRequiredService<PreviewPublications>()
            .IntendAsync(File, Video, OsHash.Compute(path), runtimeSeconds: null, TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(owed);
        Assert.Equal(0, await context.PreviewPublications.CountAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<RunResult> RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<PreviewGenerationRoutine>()
            .RunAsync(target: null, TestContext.Current.CancellationToken);
    }

    private static async Task<PreviewPublicationRow> RowAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .PreviewPublications.AsNoTracking()
            .OrderBy(row => row.IntendedAt)
            .FirstAsync(row => row.VideoFileId == File, TestContext.Current.CancellationToken);
    }

    private static PublicationStore Store(TestDatabase database) =>
        database.Services.CreateScope().ServiceProvider.GetRequiredService<PublicationStore>();

    /// <summary>
    /// A filed, identified Video File on disk, an installation that has been
    /// told what publishing sends, and — unless this is the test that does it
    /// itself — the intent Filing would have written.
    /// </summary>
    private static async Task<string> GiveAsync(
        TestDatabase database,
        bool explained = true,
        bool owed = true)
    {
        var library = Path.Combine(database.Location.DataDirectory, "library");

        Directory.CreateDirectory(library);

        var path = Path.Combine(library, "a scene.mkv");

        await System.IO.File.WriteAllBytesAsync(
            path,
            Bytes(200_000, fill: 3),
            TestContext.Current.CancellationToken);

        var hash = OsHash.Compute(path)!;

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var installation = await context.Installation.AsTracking().SingleAsync(TestContext.Current.CancellationToken);

        installation.PrdbUserHash = UserHash;
        installation.PublishGeneratedPreviews = true;
        installation.PreviewPublicationExplainedAt = explained ? database.Time.GetUtcNow() : null;

        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = Video,
            EntryDirectory = library,
            FiledAt = database.Time.GetUtcNow(),
        });

        context.VideoFiles.Add(new VideoFileRow
        {
            Id = File,
            LibraryEntryVideoId = Video,
            FiledPath = path,
            QualityLabel = "1080p",
            SizeBytes = new FileInfo(path).Length,
            RuntimeSeconds = Runtime,
            Width = 1920,
            Height = 1080,
            VideoCodec = "h264",
            OsHash = hash,
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (owed)
        {
            Assert.True(
                await scope.ServiceProvider.GetRequiredService<PreviewPublications>()
                    .IntendAsync(File, Video, hash, Runtime, TestContext.Current.CancellationToken),
                "the filed file this test is about was not owed a preview.");

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return path;
    }

    private static byte[] Bytes(int length, byte fill)
    {
        var bytes = new byte[length];

        Array.Fill(bytes, fill);

        return bytes;
    }

    /// <summary>
    /// A JPEG that is nothing but a frame header, which is all
    /// <c>JpegGeometry</c> reads and all the geometry check needs.
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

    /// <summary>
    /// Stands where ffmpeg stands and nowhere else: it is handed a path and a
    /// plan and answers with bytes, exactly as the subprocess does.
    /// </summary>
    private sealed class FakeSpriteSheetProcess(FfmpegCaptureResult result) : ISpriteSheetProcess
    {
        public FakeSpriteSheetProcess(byte[] sheet)
            : this(new FfmpegCaptureResult(0, sheet, TimedOut: false, TooLarge: false, Error: string.Empty))
        {
        }

        public int Runs { get; private set; }

        public SpriteSheetPlan? Plan { get; private set; }

        public Task<FfmpegCaptureResult> RunAsync(
            string path,
            SpriteSheetPlan plan,
            CancellationToken cancellationToken)
        {
            Runs++;
            Plan = plan;

            return Task.FromResult(result);
        }
    }

    private static Task<TestDatabase> CreateAsync(ISpriteSheetProcess ffmpeg) =>
        TestDatabase.CreateAsync(also: services =>
        {
            services.AddFabSync();
            services.AddSingleton(ffmpeg);
        });
}
