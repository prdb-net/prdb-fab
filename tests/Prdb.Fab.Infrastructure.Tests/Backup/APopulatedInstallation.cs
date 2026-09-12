using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Backup;

/// <summary>
/// An installation with something in it: one row in every exported table, and
/// two rows of cache beside them so that their absence from a document is
/// something rather than nothing.
/// </summary>
/// <remarks>
/// Shared between the export tests and the Restore tests on purpose. A round
/// trip is only worth what the installation going into it is worth, and a
/// second seed kept beside this one would drift until the two were testing
/// different installations under the same name.
/// </remarks>
public static class APopulatedInstallation
{
    public static readonly Guid Video = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    public static readonly Guid OtherVideo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
    public static readonly Guid Site = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    public static readonly Guid Indexer = Guid.Parse("0198ec28-1c00-7000-8000-000000000001");
    public static readonly Guid Rule = Guid.Parse("0198ec28-1c00-7000-8000-000000000002");
    public static readonly Guid Download = Guid.Parse("0198ec28-1c00-7000-8000-000000000003");
    public static readonly Guid OriginRule = Guid.Parse("0198ec28-1c00-7000-8000-000000000004");
    public static readonly Guid Arriving = Guid.Parse("0198ec28-1c00-7000-8000-000000000005");
    public static readonly Guid VideoFile = Guid.Parse("0198ec28-1c00-7000-8000-000000000006");
    public static readonly Guid LogEntry = Guid.Parse("0198ec28-1c00-7000-8000-000000000007");
    public static readonly Guid Preference = Guid.Parse("0198ec28-1c00-7000-8000-000000000008");

    /// <summary>
    /// One row in every exported table, and two in the cache beside them so
    /// that their absence from the document is something rather than nothing.
    /// </summary>
    public static async Task<Seeded> SeedAsync(TestDatabase database)
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

        return new Seeded(video.Id);
    }

    /// <param name="LocalVideoId">
    /// The Catalogue's own surrogate for the seeded Video, which is what the
    /// What's New marker holds in the database and not what the document
    /// carries.
    /// </param>
    public sealed record Seeded(long LocalVideoId);
}
