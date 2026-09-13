using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Fab.Infrastructure.Tests.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

/// <summary>
/// ADR 0062 in the routine: an Arriving File prdb could not name, named from a
/// moderated hash binding — and every case where it must not be.
/// </summary>
public sealed class PreviewHashIdentificationTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Identify = "/videos/identify";

    /// <summary>The hash the probe stored, in the case the probe happened to use.</summary>
    private const string Stored = "a1b2c3d4e5f60718";

    /// <summary>The same hash as prdb publishes it. Neither side is the canonical one.</summary>
    private const string Published = "A1B2C3D4E5F60718";

    private static readonly Guid Video = Guid.Parse("aaaa2222-0000-4000-8000-000000000001");
    private static readonly Guid Another = Guid.Parse("aaaa2222-0000-4000-8000-000000000002");
    private static readonly Guid Site = Guid.Parse("cccc2222-0000-4000-8000-000000000001");
    private static readonly Guid Download = Guid.Parse("dddd2222-0000-4000-8000-000000000001");
    private static readonly Guid Indexer = Guid.Parse("eeee2222-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The whole path, and the normalisation with it: the probe stored a lower
    /// case hash and prdb publishes an upper case one, which is a difference
    /// nothing in either payload admits to.
    /// </summary>
    [Fact]
    public async Task A_file_prdb_could_not_name_is_named_from_the_evidence()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        Assert.Equal(Video, arrival.VideoId);
        Assert.Equal(IdentificationConfidence.Strong, arrival.Confidence);
        Assert.Equal(IdentificationRung.PreviewHash, arrival.MatchedBy);

        // The default After-Download gate admits Strong, so it goes on to be
        // filed rather than stopping at a person.
        Assert.Equal(ArrivingFileState.AwaitingFiling, arrival.State);
        Assert.Null(arrival.Reason);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        // Nothing is reported to prdb as a person's answer. An automatic
        // outcome is not a Confirmed Assignment (ADR 0022, ADR 0062).
        Assert.Empty(await context.ConfirmedAssignments.ToListAsync(TestContext.Current.CancellationToken));

        // The provenance is recorded where the status page reads outcomes.
        Assert.Contains(
            await context.IdentificationOutcomes.ToListAsync(TestContext.Current.CancellationToken),
            row => row.Outcome == nameof(IdentificationRung.PreviewHash));
    }

    /// <summary>
    /// prdb's answer is the authority. Where it names a Video the evidence is
    /// not consulted at all — which is what makes the design correct whether or
    /// not prdb's own ladder already reads these bindings.
    /// </summary>
    [Fact]
    public async Task prdbs_own_answer_is_never_overruled()
    {
        var prdb = new FakePrdbApi().Answers(Identify, Named(One, Another));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        Assert.Equal(Another, arrival.VideoId);
        Assert.Equal(IdentificationConfidence.Exact, arrival.Confidence);
        Assert.Equal(IdentificationRung.OsHash, arrival.MatchedBy);
    }

    /// <summary>
    /// Two linked previews naming two Videos. prdb's own data disagrees with
    /// itself about this file, and a tool that picked one would be guessing —
    /// so nothing is chosen and both are recorded where the person deciding can
    /// see them.
    /// </summary>
    [Fact]
    public async Task Conflicting_evidence_chooses_nothing_and_records_both()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: [Video, Another]);
        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        Assert.Null(arrival.VideoId);
        Assert.Equal(ArrivingFileReason.Unidentified, arrival.Reason);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var candidates = await context.ArrivingFileCandidates
            .Select(row => row.VideoId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([Video, Another], [.. candidates.Order()]);
    }

    /// <summary>
    /// An unlinked preview names no Video, which is exactly what the by-hash
    /// endpoint returns and exactly why it cannot identify anything. A withdrawn
    /// one is not evidence either.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task An_unlinked_or_withdrawn_preview_is_not_evidence(bool unlinked, bool withdrawn)
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(
            database,
            arrival: true,
            evidence: [Video],
            unlinked: unlinked,
            shown: !withdrawn);

        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        Assert.Null(arrival.VideoId);
        Assert.Null(arrival.MatchedBy);
    }

    /// <summary>
    /// prdb called the file ambiguous and listed its candidates. Evidence naming
    /// one of them breaks the tie; evidence naming something else does not.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ambiguity_is_broken_only_from_inside_prdbs_own_candidates(bool inside)
    {
        var prdb = new FakePrdbApi().Answers(Identify, Ambiguous(One, inside ? Video : Another));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        if (inside)
        {
            Assert.Equal(Video, arrival.VideoId);
            Assert.Equal(IdentificationRung.PreviewHash, arrival.MatchedBy);
        }
        else
        {
            Assert.Null(arrival.VideoId);
            Assert.Equal(ArrivingFileReason.Unidentified, arrival.Reason);
        }
    }

    /// <summary>
    /// Evidence that arrives after the file did. Nothing is spent looking, so
    /// there is no cadence to tune: a file that has been in the Review Queue
    /// since Tuesday is identified on the tick after somebody opens its Video's
    /// Preview.
    /// </summary>
    [Fact]
    public async Task Evidence_that_arrives_later_reaches_a_file_already_in_the_queue()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: []);
        await RunAsync(database);

        Assert.Null((await ArrivalAsync(database)).VideoId);

        await GiveAsync(database, arrival: false, evidence: [Video]);
        await RunAsync(database);

        Assert.Equal(Video, (await ArrivalAsync(database)).VideoId);

        // And no second identify request was needed for it: the second run had
        // no new arrival to ask about.
        Assert.Single(prdb.AskedFor(Identify));
    }

    /// <summary>
    /// A gate narrowed to <c>Exact</c> is a person's decision. The Video is
    /// still named — the identification happened — and the file waits for them
    /// rather than being filed.
    /// </summary>
    [Fact]
    public async Task A_gate_that_does_not_admit_strong_names_the_video_and_still_waits()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            await context.GateAdmissions
                .Where(row => row.Gate == AfterDownloadGate.Name
                    && row.Confidence == IdentificationConfidence.Strong)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        Assert.Equal(Video, arrival.VideoId);
        Assert.Equal(ArrivingFileState.AwaitingIdentification, arrival.State);
        Assert.Equal(ArrivingFileReason.Unidentified, arrival.Reason);
    }

    /// <summary>
    /// The withdrawal check, on a file that has not been moved yet: the
    /// assignment is taken back and the file goes to the Review Queue. Nothing
    /// has been moved, so nothing has to be undone.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_before_filing_takes_the_assignment_back()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await RunAsync(database);

        Assert.Equal(Video, (await ArrivalAsync(database)).VideoId);

        await WithdrawAsync(database);
        await RunAsync(database);

        var arrival = await ArrivalAsync(database);

        Assert.Null(arrival.VideoId);
        Assert.Null(arrival.Confidence);
        Assert.Null(arrival.MatchedBy);
        Assert.Equal(ArrivingFileState.AwaitingIdentification, arrival.State);
        Assert.Equal(ArrivingFileReason.Unidentified, arrival.Reason);
    }

    /// <summary>
    /// The line ADR 0062 draws absolutely: once a file is filed, a withdrawal
    /// moves nothing on disk. The Library entry, the Video File and the path
    /// all stand, and a person is told.
    /// </summary>
    [Fact]
    public async Task A_withdrawal_after_filing_moves_nothing_and_flags_the_file()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await RunAsync(database);
        var file = await FileItAsync(database);

        await WithdrawAsync(database);
        await RunAsync(database);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            var still = await context.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);

            Assert.Equal(file, still.Id);
            Assert.Equal(Video, still.LibraryEntryVideoId);
            Assert.Equal("/library/A Site/An Entry/video.mkv", still.FiledPath);
            Assert.Equal(1, await context.LibraryEntries.CountAsync(TestContext.Current.CancellationToken));

            var flag = await context.IdentificationFlags.SingleAsync(TestContext.Current.CancellationToken);

            Assert.Equal(file, flag.VideoFileId);
            Assert.Equal(Video, flag.VideoId);
            Assert.Null(flag.NowNamesVideoId);
        }

        // And the flag goes when the evidence comes back, because a restored
        // preview is not something to be told about twice.
        await RestoreAsync(database);
        await RunAsync(database);

        await using (var scope = database.Scope())
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .IdentificationFlags.ToListAsync(TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    /// The pass spends no prdb request of its own, which is what makes it
    /// independent of the budget, of prdb being reachable, and of there being a
    /// key at all. Here the installation has no key, so the identify call never
    /// happens — and the file is still identified.
    /// </summary>
    [Fact]
    public async Task No_prdb_request_is_needed_for_the_evidence_to_be_read()
    {
        var prdb = new FakePrdbApi().Answers(Identify, NothingFound(One));

        await using var database = await CreateAsync(prdb);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Installation.ExecuteUpdateAsync(
                    row => row.SetProperty(installation => installation.PrdbApiKey, (string?)null),
                    TestContext.Current.CancellationToken);
        }

        await GiveAsync(database, arrival: true, evidence: [Video]);
        await MarkUnidentifiedAsync(database);

        // A fresh scope each time, which is what a restart is to a routine that
        // keeps nothing between runs.
        await RunAsync(database);

        Assert.Empty(prdb.Asked);

        var arrival = await ArrivalAsync(database);

        Assert.Equal(Video, arrival.VideoId);
        Assert.Equal(IdentificationRung.PreviewHash, arrival.MatchedBy);

        // And running it again changes nothing: the assignment still holds, so
        // the withdrawal check leaves it alone.
        await RunAsync(database);

        Assert.Equal(Video, (await ArrivalAsync(database)).VideoId);
    }

    private static readonly Guid One = Guid.Parse("ffff2222-0000-4000-8000-000000000001");

    private static async Task RunAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<ArrivalIdentificationRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);
    }

    private static async Task<ArrivingFileRow> ArrivalAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .ArrivingFiles.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task MarkUnidentifiedAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .ArrivingFiles
            .ExecuteUpdateAsync(
                row => row.SetProperty(arrival => arrival.Reason, ArrivingFileReason.Unidentified),
                TestContext.Current.CancellationToken);
    }

    private static async Task WithdrawAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .UserPreviews
            .ExecuteUpdateAsync(
                row => row.SetProperty(preview => preview.Shown, false),
                TestContext.Current.CancellationToken);
    }

    private static async Task RestoreAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .UserPreviews
            .ExecuteUpdateAsync(
                row => row.SetProperty(preview => preview.Shown, true),
                TestContext.Current.CancellationToken);
    }

    /// <summary>Files the arrival by hand, the way the Filing routine would.</summary>
    private static async Task<Guid> FileItAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var file = Guid.NewGuid();

        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = Video,
            EntryDirectory = "/library/A Site/An Entry",
            FiledAt = Noon,
        });
        context.VideoFiles.Add(new VideoFileRow
        {
            Id = file,
            LibraryEntryVideoId = Video,
            FiledPath = "/library/A Site/An Entry/video.mkv",
            QualityLabel = "1080p",
            SizeBytes = 1024,
            OsHash = Stored,
        });

        await context.ArrivingFiles.ExecuteUpdateAsync(
            row => row
                .SetProperty(arrival => arrival.State, ArrivingFileState.Filed)
                .SetProperty(arrival => arrival.IsOnDisk, false),
            TestContext.Current.CancellationToken);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return file;
    }

    private static async Task GiveAsync(
        TestDatabase database,
        bool arrival,
        IReadOnlyList<Guid> evidence,
        bool unlinked = false,
        bool shown = true)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        if (arrival)
        {
            if (!await context.CatalogueSites.AnyAsync(
                    row => row.PrdbId == Site,
                    TestContext.Current.CancellationToken))
            {
                context.CatalogueSites.Add(new CatalogueSiteRow { PrdbId = Site, Title = "A Site" });
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var site = await context.CatalogueSites.SingleAsync(
                row => row.PrdbId == Site,
                TestContext.Current.CancellationToken);

            foreach (var video in new[] { Video, Another })
            {
                context.CatalogueVideos.Add(new CatalogueVideoRow
                {
                    PrdbId = video,
                    Title = "A scene",
                    NormalisedTitle = "a scene",
                    SiteId = site.Id,
                });
            }

            context.ArrivingFiles.Add(new ArrivingFileRow
            {
                Id = One,
                DownloadId = Download,
                IndexerId = Indexer,
                DerivedReleaseId = "release",
                SourcePath = "/downloads/video.mkv",
                ArrivedName = "video.mkv",
                State = ArrivingFileState.AwaitingIdentification,
                SizeBytes = 1024,
                QualityLabel = "1080p",
                OsHash = Stored,
                ProbeOutcome = ProbeOutcome.Read,
            });
        }

        var order = 0;

        foreach (var video in evidence)
        {
            context.UserPreviews.Add(new UserPreviewRow
            {
                PrdbId = Guid.NewGuid(),
                VideoPrdbId = unlinked ? null : video,
                OsHash = Published,
                Kind = UserPreviewAsset.Single,
                Url = "https://cdn.example/a.jpg",
                Width = 1280,
                Height = 720,
                DisplayOrder = order++,
                ShownUnder = UserPreviewModeration.Signature("Approved", "Public"),
                Shown = shown,
                UpdatedAtUtc = Noon,
                CreatedAtUtc = Noon,
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdbApi prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddFabSync());

        await using var scope = database.Scope();

        await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.ExecuteUpdateAsync(
                row => row.SetProperty(installation => installation.PrdbApiKey, ApiKey),
                TestContext.Current.CancellationToken);

        return database;
    }

    private static string NothingFound(Guid reference) => $$"""
        {"results":[{"ref":"{{reference:D}}","confidence":0,"candidates":[]}]}
        """;

    private static string Named(Guid reference, Guid video) => $$$"""
        {
          "results": [
            {
              "ref": "{{{reference:D}}}",
              "videoId": "{{{video:D}}}",
              "confidence": 4,
              "matchedBy": 0,
              "candidates": [],
              "video": {
                "id": "{{{video:D}}}",
                "title": "Named",
                "preNames": [],
                "actors": [],
                "images": []
              }
            }
          ]
        }
        """;

    private static string Ambiguous(Guid reference, Guid candidate) => $$"""
        {"results":[{"ref":"{{reference:D}}","confidence":5,"candidates":["{{candidate:D}}"]}]}
        """;
}
