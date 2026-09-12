using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Backup;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Infrastructure.Backup;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Backup;

/// <summary>
/// ADR 0009's document, and the three things ADR 0033 requires of it: the
/// boundary runs between tables, every reference out of it is an identifier
/// somebody outside owns, and the format is not the schema.
/// </summary>
public sealed class BackupTests
{
    private static readonly Guid Video = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OtherVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    private static readonly Guid Site = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid Indexer = Guid.Parse("0198ec28-1c00-7000-8000-000000000001");
    private static readonly Guid Rule = Guid.Parse("0198ec28-1c00-7000-8000-000000000002");
    private static readonly Guid Download = Guid.Parse("0198ec28-1c00-7000-8000-000000000003");
    private static readonly Guid OriginRule = Guid.Parse("0198ec28-1c00-7000-8000-000000000004");
    private static readonly Guid Arriving = Guid.Parse("0198ec28-1c00-7000-8000-000000000005");
    private static readonly Guid VideoFile = Guid.Parse("0198ec28-1c00-7000-8000-000000000006");
    private static readonly Guid LogEntry = Guid.Parse("0198ec28-1c00-7000-8000-000000000007");
    private static readonly Guid Preference = Guid.Parse("0198ec28-1c00-7000-8000-000000000008");

    /// <summary>
    /// The mechanical half of "generate or verify the table list from the
    /// model". A table that begins crossing the boundary fails here, and so does
    /// a section of the document that no table fills — which is the same mistake
    /// read from the other end.
    /// </summary>
    [Fact]
    public void Every_exported_table_has_a_section_and_every_section_a_table()
    {
        Assert.Empty(BackupSections.Unaccounted(TheModel()));
        Assert.Equal(15, BackupSections.Carried.Count);
    }

    /// <summary>
    /// And the check is not decoration: a Backup refuses to be built while the
    /// pairing and the model disagree, rather than writing a document with a
    /// hole where a table should be.
    /// </summary>
    [Fact]
    public void A_table_without_a_section_is_a_refusal_rather_than_a_gap()
    {
        var complaint = Assert.Throws<InvalidOperationException>(
            () => BackupSections.MustCover(WithoutASection()));

        Assert.Contains("session", complaint.Message, StringComparison.Ordinal);
        Assert.Contains("no section of the document carries it", complaint.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same check one level down, and this is what says it is not vacuous:
    /// a table that stops holding a column the document still carries is a
    /// refusal too. ADR 0033 exports a table whole, so a column is not
    /// something the document may quietly disagree about.
    /// </summary>
    [Fact]
    public void A_column_the_document_cannot_place_is_a_refusal_too()
    {
        var builder = new ModelBuilder();

        builder.Entity<IndexerRow>().ToTable("indexer");
        builder.Entity<IndexerRow>().Ignore(row => row.DailyQueryBudget);
        builder.Entity<IndexerRow>().Declares(ExportClass.Exported);

        Assert.Contains(
            BackupSections.Unaccounted(builder.FinalizeModel()),
            complaint => complaint.Contains("Indexers.DailyQueryBudget", StringComparison.Ordinal)
                && complaint.Contains("does not hold", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_exported_table_is_in_the_document_and_no_other_table_is()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database);

        var document = await ReadAsync(database);

        Assert.Equal(BackupFormat.Version, document.FormatVersion);
        Assert.Equal(database.Time.GetUtcNow(), document.WrittenAt);
        Assert.False(string.IsNullOrWhiteSpace(document.ToolVersion));

        Assert.Equal("/library", document.Installation.LibraryRoot);
        Assert.Equal("hashed", document.Installation.PasswordHash);
        Assert.Equal("prdb-key", document.Installation.PrdbApiKey);
        Assert.Equal(OnboardingStep.Complete, document.Installation.OnboardingStep);

        // ADR 0006's admission sets ship as data, so the document carries them
        // without anything in this test writing one.
        Assert.NotEmpty(document.GateAdmissions);

        Assert.Equal("An Indexer", Assert.Single(document.Indexers).Name);
        Assert.Equal("indexer-key", Assert.Single(document.Indexers).ApiKey);
        Assert.Equal("A Rule", Assert.Single(document.AutomationRules).Name);
        Assert.Equal(
            new BackupAutomationRuleIndexer(Rule, Indexer),
            Assert.Single(document.AutomationRuleIndexers));
        Assert.Equal(Video, Assert.Single(document.LibraryEntries).VideoId);
        Assert.Equal("quality", Assert.Single(document.VideoFiles).QualityLabel);
        Assert.Equal("A.Release.1080p", Assert.Single(document.Downloads).SubmittedName);
        Assert.Equal("A Rule", Assert.Single(document.DownloadOriginRules).RuleName);
        Assert.Equal(ArrivingFileState.AwaitingFiling, Assert.Single(document.ArrivingFiles).State);
        Assert.Equal(
            new BackupArrivingFileCandidate(Arriving, OtherVideo),
            Assert.Single(document.ArrivingFileCandidates));
        Assert.Equal(FulfilmentQuality.P1080, Assert.Single(document.ReportedStates).Quality);
        Assert.Equal("os-hash", Assert.Single(document.ConfirmedAssignments).OsHash);
        Assert.Equal("Moved", Assert.Single(document.OperationLog).Act);
        Assert.Equal(AccountPreferenceKind.WantedVideo, Assert.Single(document.AccountPreferenceWrites).Kind);

        // Nothing from a cache table has a section of its own, which is the
        // other half of ADR 0033's boundary: the Catalogue, the Indexer Cache,
        // the Artwork Cache and the Routine history are all refetchable and all
        // absent.
        var written = JsonSerializer.Deserialize<JsonElement>(BackupJson.Write(document));
        var sections = written.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(
            [
                "formatVersion",
                "toolVersion",
                "writtenAt",
                "installation",
                "gateAdmissions",
                "indexers",
                "automationRules",
                "automationRuleIndexers",
                "libraryEntries",
                "videoFiles",
                "downloads",
                "downloadOriginRules",
                "arrivingFiles",
                "arrivingFileCandidates",
                "reportedStates",
                "confirmedAssignments",
                "operationLog",
                "accountPreferenceWrites",
            ],
            sections);
    }

    /// <summary>
    /// ADR 0033: absolute in the database, root-relative in the document, and
    /// the root travels with the path because one Operation Log entry spans
    /// both of them.
    /// </summary>
    [Fact]
    public async Task Paths_are_carried_against_the_root_each_belongs_to()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database);

        var document = await ReadAsync(database);
        var roots = new BackupRoots("/library", "/downloads");

        Assert.Equal(
            new BackupPath(BackupRoot.Library, "A Site/An Entry"),
            Assert.Single(document.LibraryEntries).EntryDirectory);
        Assert.Equal(
            new BackupPath(BackupRoot.Library, "A Site/An Entry/video.mkv"),
            Assert.Single(document.VideoFiles).FiledPath);
        Assert.Equal(
            new BackupPath(BackupRoot.Downloads, "A.Release.1080p/video.mkv"),
            Assert.Single(document.ArrivingFiles).SourcePath);
        Assert.Equal(
            new BackupPath(BackupRoot.Library, "A Site/An Entry/video.mkv"),
            Assert.Single(document.ArrivingFiles).IntendedPath);

        var logged = Assert.Single(document.OperationLog);
        Assert.Equal(new BackupPath(BackupRoot.Downloads, "A.Release.1080p/video.mkv"), logged.PathBefore);
        Assert.Equal(new BackupPath(BackupRoot.Library, "A Site/An Entry/video.mkv"), logged.PathAfter);
        Assert.Null(logged.DisplacedPath);

        // The same roots put every one of them back where it came from, which
        // is what Restore will do with roots the user re-answers.
        Assert.Equal("/downloads/A.Release.1080p/video.mkv", roots.Absolute(logged.PathBefore!));
        Assert.Equal("/library/A Site/An Entry/video.mkv", roots.Absolute(logged.PathAfter!));
    }

    /// <summary>
    /// ADR 0033's closure rule, at the one place in the schema where an exported
    /// row holds a local surrogate: the What's New marker points at a Catalogue
    /// row by its integer id, and the document carries prdb's own id instead.
    /// Without the translation a restored installation would point at whatever
    /// row happened to land on that integer.
    /// </summary>
    [Fact]
    public async Task The_whats_new_marker_crosses_the_boundary_as_prdbs_video_id()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeded = await SeedAsync(database);

        var document = await ReadAsync(database);

        Assert.Equal(Video, document.Installation.WhatsNewObservedVideo);
        Assert.Equal(database.Time.GetUtcNow(), document.Installation.WhatsNewObservedAt);

        // The local surrogate is the one thing that must not travel: the
        // integer means whatever the restored Catalogue happens to put on it.
        Assert.DoesNotContain(
            $"\"whatsNewObservedVideo\": {seeded.LocalVideoId}",
            BackupJson.Write(document),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0009: the format version is not the EF schema version, or every
    /// migration would be a format break. <c>Core</c> cannot see EF at all
    /// (ADR 0035, and <c>ArchitectureTests.Core_declares_no_dependencies</c>),
    /// so what is left to assert is that nothing about the schema leaked into
    /// the envelope.
    /// </summary>
    [Fact]
    public async Task The_format_version_says_nothing_about_the_schema()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database);

        var document = await ReadAsync(database);
        var written = BackupJson.Write(document);

        Assert.Equal(1, document.FormatVersion);

        await using var scope = database.Scope();
        var applied = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        foreach (var migration in applied)
        {
            Assert.DoesNotContain(migration, written, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The document survives being written and read again. Compared as text
    /// rather than as records, because a record holding lists compares its
    /// lists by reference — and text is what actually travels.
    /// </summary>
    [Fact]
    public async Task A_document_round_trips_through_its_file()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database);

        var written = BackupJson.Write(await ReadAsync(database));
        var read = BackupJson.Read(written);

        Assert.NotNull(read);
        Assert.Equal(written, BackupJson.Write(read));
        Assert.Equal(Video, Assert.Single(read.LibraryEntries).VideoId);
        Assert.Equal(
            new BackupPath(BackupRoot.Downloads, "A.Release.1080p/video.mkv"),
            Assert.Single(read.ArrivingFiles).SourcePath);
        Assert.Equal("[\"leftover.nfo\"]", Assert.Single(read.OperationLog).LeftoverNamesJson);
    }

    /// <summary>
    /// ADR 0009 wants a document a person can open and read. Two properties of
    /// that: an enumeration is its name rather than its ordinal, and a title
    /// nobody can spell in ASCII is not escaped into hexadecimal.
    /// </summary>
    [Fact]
    public async Task The_document_is_written_to_be_read()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database);

        var written = BackupJson.Write(await ReadAsync(database));

        Assert.Contains("\"onboardingStep\": \"Complete\"", written, StringComparison.Ordinal);
        Assert.Contains("Müller & Söhne", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00", written, StringComparison.Ordinal);
        Assert.Contains("\n", written, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing prdb holds is in the file: not the Catalogue, not the Indexer
    /// Cache. A Backup of an installation that holds both carries neither.
    /// </summary>
    [Fact]
    public async Task Nothing_the_tool_can_fetch_again_is_in_the_document()
    {
        await using var database = await TestDatabase.CreateAsync();
        await SeedAsync(database);

        var written = BackupJson.Write(await ReadAsync(database));

        Assert.DoesNotContain("A Catalogue Title", written, StringComparison.Ordinal);
        Assert.DoesNotContain("A Cached Release", written, StringComparison.Ordinal);
    }

    private static async Task<BackupDocument> ReadAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<Backups>()
            .ReadAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One row in every exported table, and two in the cache beside them so
    /// that their absence from the document is something rather than nothing.
    /// </summary>
    private static async Task<Seeded> SeedAsync(TestDatabase database)
    {
        var now = database.Time.GetUtcNow();
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        var site = new CatalogueSiteRow { PrdbId = Site, Title = "A Site" };
        context.Add(site);
        context.Add(new IndexerRow
        {
            Id = Indexer,
            Name = "An Indexer",
            Url = "https://indexer.invalid/api",
            ApiKey = "indexer-key",
            Categories = "Adult",
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = now,
            Rank = 1,
        });
        context.Add(new AutomationRuleRow
        {
            Id = Rule,
            Name = "A Rule",
            Enabled = true,
            MinimumSize = 1_000,
            MaximumSize = 2_000,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var video = new CatalogueVideoRow
        {
            PrdbId = Video,
            Title = "A Catalogue Title",
            NormalisedTitle = "a catalogue title",
            SiteId = site.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        context.Add(video);
        context.Add(new AutomationRuleIndexerRow { AutomationRuleId = Rule, IndexerId = Indexer });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Add(new ReleaseRow
        {
            IndexerId = Indexer,
            DerivedReleaseId = "release-1",
            RawGuid = "release-1",
            Title = "A Cached Release",
            NormalisedTitle = "a cached release",
            Categories = "[]",
            PostDate = now,
            PubDate = now,
            DownloadUrl = "https://indexer.invalid/nzb",
            FirstSeenAt = now,
            IdentificationState = IdentificationState.Matched,
            VideoId = video.Id,
            Confidence = IdentificationConfidence.Exact,
            MatchedBy = IdentificationRung.ReleaseName,
        });
        context.Add(new LibraryEntryRow
        {
            VideoId = Video,
            EntryDirectory = "/library/A Site/An Entry",
            FiledAt = now,
        });
        context.Add(new DownloadRow
        {
            Id = Download,
            VideoId = Video,
            IndexerId = Indexer,
            DerivedReleaseId = "release-1",
            SubmittedName = "A.Release.1080p",
            NzoId = "SABnzbd_nzo_1",
            State = DownloadState.Collected,
            OutstandingSince = now,
            OriginIsPerson = false,
            CreatedAt = now,
        });
        context.Add(new ArrivingFileRow
        {
            Id = Arriving,
            DownloadId = Download,
            IndexerId = Indexer,
            DerivedReleaseId = "release-1",
            SourcePath = "/downloads/A.Release.1080p/video.mkv",
            ArrivedName = "video.mkv",
            State = ArrivingFileState.AwaitingFiling,
            VideoId = Video,
            SiteId = Site,
            SizeBytes = 3_000,
            IntendedPath = "/library/A Site/An Entry/video.mkv",
            ProbeOutcome = ProbeOutcome.Read,
        });
        context.Add(new ReportedStateRow
        {
            VideoId = Video,
            UserHash = "user-hash",
            IsFulfilled = true,
            Quality = FulfilmentQuality.P1080,
            FulfilledAt = now,
        });
        context.Add(new ConfirmedAssignmentRow
        {
            OsHash = "os-hash",
            VideoId = Video,
            UserHash = "user-hash",
            SizeBytes = 3_000,
            ArrivalFileName = "video.mkv",
            ReleaseName = "A.Release.1080p",
            SentAt = now,
        });
        context.Add(new OperationLogEntryRow
        {
            Id = LogEntry,
            Act = "Moved",
            VideoFileId = VideoFile,
            LibraryEntryVideoId = Video,
            VideoId = Video,
            DownloadId = Download,
            PathBefore = "/downloads/A.Release.1080p/video.mkv",
            PathAfter = "/library/A Site/An Entry/video.mkv",
            LeftoverNamesJson = "[\"leftover.nfo\"]",
            Actor = "Automation",
            Reason = "Filed",
            At = now,
        });
        context.Add(new AccountPreferenceWriteRow
        {
            Id = Preference,
            Kind = AccountPreferenceKind.WantedVideo,
            EntityId = Video,
            Desired = true,
            RequestedAt = now,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Add(new VideoFileRow
        {
            Id = VideoFile,
            LibraryEntryVideoId = Video,
            FiledPath = "/library/A Site/An Entry/video.mkv",
            QualityLabel = "quality",
            SizeBytes = 3_000,
            OsHash = "os-hash",
        });
        context.Add(new DownloadOriginRuleRow
        {
            Id = OriginRule,
            DownloadId = Download,
            AutomationRuleId = Rule,
            RuleName = "A Rule",
        });
        context.Add(new ArrivingFileCandidateRow { ArrivingFileId = Arriving, VideoId = OtherVideo });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await context.Installation.ExecuteUpdateAsync(
            update => update
                .SetProperty(row => row.PasswordHash, "hashed")
                .SetProperty(row => row.PrdbApiKey, "prdb-key")
                .SetProperty(row => row.PrdbUserHash, "user-hash")
                .SetProperty(row => row.LibraryRoot, "/library")
                .SetProperty(row => row.SabnzbdUrl, "http://sabnzbd.invalid")
                .SetProperty(row => row.SabnzbdApiKey, "sabnzbd-key")
                .SetProperty(row => row.SabnzbdCategory, "Müller & Söhne")
                .SetProperty(row => row.PathMappingFrom, "/remote/complete")
                .SetProperty(row => row.PathMappingTo, "/downloads")
                .SetProperty(row => row.OnboardingStep, OnboardingStep.Complete)
                .SetProperty(row => row.WhatsNewObservedAt, now)
                .SetProperty(row => row.WhatsNewObservedVideoId, video.Id),
            TestContext.Current.CancellationToken);

        return new(video.Id);
    }

    /// <summary>
    /// The model with one exported table the pairing does not know about. The
    /// <c>Session</c> row is the one table ADR 0010 keeps out of the Backup on
    /// purpose, which makes it the honest stand-in for a table somebody exports
    /// later and forgets to carry.
    /// </summary>
    private static IModel WithoutASection()
    {
        var builder = new ModelBuilder();

        builder.Entity<SessionRow>().ToTable("session");
        builder.Entity<SessionRow>().Declares(ExportClass.Exported);

        return builder.FinalizeModel();
    }

    private static IModel TheModel()
    {
        var options = new DbContextOptionsBuilder<FabDbContext>()
            .UseSqlite("Data Source=schema-only.db")
            .Options;

        using var context = new FabDbContext(options);

        return context.Model;
    }

    private sealed record Seeded(long LocalVideoId);
}
