using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Reporting;
using Prdb.Fab.Infrastructure.Tests.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Reporting;

public sealed class ReportingTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string UserHash = "5b1f0c2e9a7d4f3b8c6e1a0d2f4b6a8c";
    private const string OtherUserHash = "9d3a7c1e5f8b2046ae7c9b1d3f5a7c90";
    private const string FulfilmentsPath = "/wanted-videos/fulfillments";
    private const string AssignmentsPath = "/videos/filehash-submissions";
    private static readonly Guid VideoId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset FiledAt = new(2026, 8, 20, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Both_channels_default_on()
    {
        var prdb = new FakePrdbApi();
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);

        await using var scope = database.Scope();
        var settings = await scope.ServiceProvider.GetRequiredService<ReportingSettings>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(settings.ReportFulfilments);
        Assert.True(settings.ReportConfirmedAssignments);
        Assert.Equal(0, settings.FulfilmentBacklog);
        Assert.Equal(0, settings.ConfirmedAssignmentBacklog);
        Assert.Empty(prdb.Requests);
    }

    [Fact]
    public async Task Later_migrations_preserve_saved_reporting_choices()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: "ReportingDelivery");

        await using (var arrange = database.Scope())
        {
            var context = arrange.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE installation SET ReportFulfilments = 0, ReportConfirmedAssignments = 0;",
                TestContext.Current.CancellationToken);
        }

        await using (var migrate = database.Scope())
        {
            await migrate.ServiceProvider.GetRequiredService<FabDbContext>()
                .Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using var assertScope = database.Scope();
        var saved = await assertScope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Installation.SingleAsync(TestContext.Current.CancellationToken);

        Assert.False(saved.ReportFulfilments);
        Assert.False(saved.ReportConfirmedAssignments);
    }

    [Fact]
    public async Task Enabling_a_channel_marks_the_one_routine_due_without_sending_from_settings()
    {
        var prdb = new FakePrdbApi();
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await using (var arrange = database.Scope())
        {
            var context = arrange.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Routines.Add(new RoutineRow
            {
                Name = ReportingRoutine.RoutineName,
                Lane = Lane.Sync,
                DueAt = DateTimeOffset.MaxValue,
            });
            var installation = await context.Installation.AsTracking()
                .SingleAsync(TestContext.Current.CancellationToken);
            installation.ReportFulfilments = false;
            installation.ReportConfirmedAssignments = false;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var act = database.Scope())
        {
            await act.ServiceProvider.GetRequiredService<ReportingSettings>()
                .SaveAsync(true, false, TestContext.Current.CancellationToken);
        }

        await using var assertScope = database.Scope();
        var row = await assertScope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Routines.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(database.Time.GetUtcNow(), row.DueAt);
        Assert.Empty(prdb.Requests);
    }

    [Fact]
    public async Task Fulfilment_is_one_claim_for_the_entry_and_is_not_repeated_after_acceptance()
    {
        var prdb = new FakePrdbApi().Answers(
            FulfilmentsPath,
            $$"""{"results":[{"videoId":"{{VideoId}}","outcome":0}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await ArrangeFulfilmentAsync(database, "720p", enabled: true, additionalQuality: "1440p");

        await using var scope = database.Scope();
        var routine = scope.ServiceProvider.GetRequiredService<ReportingRoutine>();

        Assert.Equal(1, (await routine.RunAsync(null, TestContext.Current.CancellationToken)).ItemsHandled);
        Assert.Equal(RunResult.NothingToDo, await routine.RunAsync(null, TestContext.Current.CancellationToken));

        var request = Assert.Single(prdb.AskingFor(FulfilmentsPath));
        using var json = JsonDocument.Parse(request.Body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(VideoId, item.GetProperty("videoId").GetGuid());
        Assert.True(item.GetProperty("isFulfilled").GetBoolean());
        Assert.Equal(1, item.GetProperty("fulfilledInQuality").GetInt32());
        Assert.Equal(3, item.GetProperty("fulfillmentByApp").GetInt32());
        Assert.Equal(FiledAt, item.GetProperty("fulfilledAtUtc").GetDateTimeOffset());
        Assert.True(
            !item.TryGetProperty("fulfillmentExternalId", out var external)
            || external.ValueKind is JsonValueKind.Null);

        var state = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .ReportedStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(FulfilmentQuality.P1080, state.Quality);
        Assert.Equal(FiledAt, state.FulfilledAt);
        Assert.Null(state.TerminalOutcome);
    }

    [Fact]
    public async Task Below_720p_is_reported_held_without_an_invented_quality()
    {
        var prdb = new FakePrdbApi().Answers(
            FulfilmentsPath,
            $$"""{"results":[{"videoId":"{{VideoId}}","outcome":1}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await ArrangeFulfilmentAsync(database, "480p", enabled: true);

        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<ReportingRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(Assert.Single(prdb.AskingFor(FulfilmentsPath)).Body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(
            !item.TryGetProperty("fulfilledInQuality", out var quality)
            || quality.ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task Removing_a_library_entry_retracts_its_reported_fulfilment()
    {
        var prdb = new FakePrdbApi()
            .Answers(
                FulfilmentsPath,
                $$"""{"results":[{"videoId":"{{VideoId}}","outcome":0}]}""")
            .Answers(
                FulfilmentsPath,
                $$"""{"results":[{"videoId":"{{VideoId}}","outcome":0}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await ArrangeFulfilmentAsync(database, "1080p", enabled: true);

        await using (var reportHeld = database.Scope())
        {
            await reportHeld.ServiceProvider.GetRequiredService<ReportingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
        }
        await using (var remove = database.Scope())
        {
            var context = remove.ServiceProvider.GetRequiredService<FabDbContext>();
            var entry = await context.LibraryEntries.SingleAsync(TestContext.Current.CancellationToken);
            context.LibraryEntries.Remove(entry);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using (var retract = database.Scope())
        {
            var result = await retract.ServiceProvider.GetRequiredService<ReportingRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
            Assert.Equal(1, result.ItemsHandled);
        }

        var requests = prdb.AskingFor(FulfilmentsPath);
        Assert.Equal(2, requests.Count);
        using var json = JsonDocument.Parse(requests[1].Body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, item.GetProperty("videoId").GetGuid());
        Assert.False(item.GetProperty("isFulfilled").GetBoolean());
        Assert.True(
            !item.TryGetProperty("fulfilledAtUtc", out var at)
            || at.ValueKind is JsonValueKind.Null);
        Assert.True(
            !item.TryGetProperty("fulfilledInQuality", out var quality)
            || quality.ValueKind is JsonValueKind.Null);

        await using var check = database.Scope();
        var state = await check.ServiceProvider.GetRequiredService<FabDbContext>()
            .ReportedStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(state.IsFulfilled);
        Assert.Null(state.Quality);
        Assert.Null(state.FulfilledAt);
    }

    [Fact]
    public async Task A_response_lost_after_remote_acceptance_converges_on_the_repeated_state()
    {
        var prdb = new FakePrdbApi()
            .Answers(FulfilmentsPath, """{"results":[]}""")
            .Answers(
                FulfilmentsPath,
                $$"""{"results":[{"videoId":"{{VideoId}}","outcome":1}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await ArrangeFulfilmentAsync(database, "1080p", enabled: true);

        await using var scope = database.Scope();
        var routine = scope.ServiceProvider.GetRequiredService<ReportingRoutine>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            routine.RunAsync(null, TestContext.Current.CancellationToken));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .ReportedStates.ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, (await routine.RunAsync(null, TestContext.Current.CancellationToken)).ItemsHandled);
        Assert.Equal(2, prdb.AskingFor(FulfilmentsPath).Count);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .ReportedStates.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Confirmed_assignment_uses_recorded_facts_and_a_terminal_disagreement_is_not_retried()
    {
        var prdb = new FakePrdbApi().Answers(
            AssignmentsPath,
            $$"""{"results":[{"videoId":"{{VideoId}}","osHash":"AABBCCDDEEFF0011","outcome":2}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await ArrangeAssignmentAsync(database, UserHash, enabled: true);
        await ArrangeAssignmentAsync(database, OtherUserHash, enabled: true, osHash: "1122334455667788");

        await using var scope = database.Scope();
        var routine = scope.ServiceProvider.GetRequiredService<ReportingRoutine>();
        var result = await routine.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Contains("Conflicted", result.Reason, StringComparison.Ordinal);
        Assert.Equal(RunResult.NothingToDo, await routine.RunAsync(null, TestContext.Current.CancellationToken));

        var request = Assert.Single(prdb.AskingFor(AssignmentsPath));
        using var json = JsonDocument.Parse(request.Body);
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(VideoId, item.GetProperty("videoId").GetGuid());
        Assert.Equal("AABBCCDDEEFF0011", item.GetProperty("osHash").GetString());
        Assert.Equal(1_234_567, item.GetProperty("filesize").GetInt64());
        Assert.Equal(0, item.GetProperty("source").GetInt32());
        Assert.Equal(123_000, item.GetProperty("durationMs").GetInt64());
        Assert.Equal(1920, item.GetProperty("width").GetInt32());
        Assert.Equal(1080, item.GetProperty("height").GetInt32());
        Assert.Equal("h264", item.GetProperty("videoCodec").GetString());
        Assert.Equal("arrival.mkv", item.GetProperty("filename").GetString());
        Assert.Equal("A.Release", item.GetProperty("releaseName").GetString());

        var rows = await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .ConfirmedAssignments.OrderBy(row => row.UserHash)
            .ToListAsync(TestContext.Current.CancellationToken);
        var sent = Assert.Single(rows, row => row.UserHash == UserHash);
        var other = Assert.Single(rows, row => row.UserHash == OtherUserHash);
        Assert.Equal("Conflicted", sent.PrdbAnswer);
        Assert.NotNull(sent.SentAt);
        Assert.Null(other.SentAt);
    }

    [Fact]
    public async Task One_turn_serves_both_enabled_channels_without_starving_either()
    {
        var prdb = new FakePrdbApi()
            .Answers(
                FulfilmentsPath,
                $$"""{"results":[{"videoId":"{{VideoId}}","outcome":0}]}""")
            .Answers(
                AssignmentsPath,
                $$"""{"results":[{"videoId":"{{VideoId}}","osHash":"AABBCCDDEEFF0011","outcome":0}]}""");
        await using var database = await TestDatabase.CreateAsync(prdb: prdb);
        await ArrangeFulfilmentAsync(database, "1080p", enabled: true);
        await ArrangeAssignmentAsync(database, UserHash, enabled: true);

        await using var scope = database.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<ReportingRoutine>()
            .RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.ItemsHandled);
        Assert.Single(prdb.AskingFor(FulfilmentsPath));
        Assert.Single(prdb.AskingFor(AssignmentsPath));
    }

    [Fact]
    public async Task Turning_channels_off_deletes_neither_sent_state_nor_confirmations()
    {
        await using var database = await TestDatabase.CreateAsync();
        await ArrangeAssignmentAsync(database, UserHash, enabled: true);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        context.ReportedStates.Add(new ReportedStateRow
        {
            VideoId = VideoId,
            UserHash = UserHash,
            IsFulfilled = true,
            Quality = FulfilmentQuality.P1080,
            FulfilledAt = FiledAt,
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await scope.ServiceProvider.GetRequiredService<ReportingSettings>()
            .SaveAsync(false, false, TestContext.Current.CancellationToken);

        Assert.Equal(1, await context.ReportedStates.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.ConfirmedAssignments.CountAsync(TestContext.Current.CancellationToken));
    }

    private static async Task ArrangeFulfilmentAsync(
        TestDatabase database,
        string quality,
        bool enabled,
        string? additionalQuality = null)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        installation.PrdbApiKey = ApiKey;
        installation.PrdbUserHash = UserHash;
        installation.ReportFulfilments = enabled;

        var video = new CatalogueVideoRow
        {
            PrdbId = VideoId,
            Title = "A Video",
            NormalisedTitle = "a video",
        };
        context.CatalogueVideos.Add(video);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.WantedVideos.Add(new WantedVideoRow { VideoId = video.Id, SinceAt = FiledAt });
        context.LibraryEntries.Add(new LibraryEntryRow
        {
            VideoId = VideoId,
            EntryDirectory = "/a/mount/that/need/not-exist",
            FiledAt = FiledAt,
        });
        context.VideoFiles.Add(File(quality, 1));
        if (additionalQuality is not null)
        {
            context.VideoFiles.Add(File(additionalQuality, 2));
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static VideoFileRow File(string quality, int suffix) => new()
    {
        Id = Guid.Parse($"0198ec28-1c00-7000-8000-{suffix:D12}"),
        LibraryEntryVideoId = VideoId,
        FiledPath = $"/a/mount/that/need/not-exist/video-{suffix}.mkv",
        QualityLabel = quality,
        SizeBytes = 1_234_567,
    };

    private static async Task ArrangeAssignmentAsync(
        TestDatabase database,
        string userHash,
        bool enabled,
        string osHash = "AABBCCDDEEFF0011")
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var installation = await context.Installation.AsTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        installation.PrdbApiKey = ApiKey;
        installation.PrdbUserHash = UserHash;
        installation.ReportConfirmedAssignments = enabled;
        context.ConfirmedAssignments.Add(new ConfirmedAssignmentRow
        {
            OsHash = osHash,
            VideoId = VideoId,
            UserHash = userHash,
            SizeBytes = 1_234_567,
            ArrivalFileName = "arrival.mkv",
            ReleaseName = "A.Release",
            RuntimeSeconds = 123,
            Width = 1920,
            Height = 1080,
            VideoCodec = "h264",
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
