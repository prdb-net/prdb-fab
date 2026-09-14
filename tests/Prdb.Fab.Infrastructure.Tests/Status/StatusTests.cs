using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Status;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Status;

public sealed class StatusTests
{
    [Fact]
    public async Task Incomplete_recent_window_sources_are_visible_as_gaps()
    {
        await using var database = await TestDatabase.CreateAsync();
        var indexerId = Guid.NewGuid();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PrdbApiKey, "configured"),
                TestContext.Current.CancellationToken);
            context.Indexers.Add(new IndexerRow
            {
                Id = indexerId,
                Name = "Fixture",
                Url = "https://indexer.invalid",
                ApiKey = "fixture",
                Categories = "Adult",
                LastVerdict = IndexerConnectionOutcome.Saved,
                LastCheckedAt = database.Time.GetUtcNow(),
            });
            context.IndexerWalkStates.Add(new IndexerWalkStateRow
            {
                IndexerId = indexerId,
                CapsTree = "[]",
                ResolvedCategoryIds = "[]",
                MissingCategoryNames = "[]",
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            status.Stages.Single(stage => stage.Id == "sync-prdb").Gaps,
            gap => gap.Title == "The prdb Recent Window is incomplete");
        Assert.Contains(
            status.Stages.Single(stage => stage.Id == "sync-indexers").Gaps,
            gap => gap.Title == "Fixture's Recent Window is incomplete");
    }

    [Fact]
    public async Task Complete_source_passes_are_not_ready_while_recent_pipeline_work_remains()
    {
        await using var database = await TestDatabase.CreateAsync();
        var indexerId = Guid.NewGuid();
        var now = database.Time.GetUtcNow();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PrdbApiKey, "configured"),
                TestContext.Current.CancellationToken);
            await context.RecentWindowState.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.CatalogueCompletedAt, now),
                TestContext.Current.CancellationToken);
            context.Indexers.Add(new IndexerRow
            {
                Id = indexerId,
                Name = "Fixture",
                Url = "https://indexer.invalid",
                ApiKey = "fixture",
                Categories = "Adult",
                LastVerdict = IndexerConnectionOutcome.Saved,
                LastCheckedAt = now,
            });
            context.IndexerWalkStates.Add(new IndexerWalkStateRow
            {
                IndexerId = indexerId,
                CapsTree = "[]",
                ResolvedCategoryIds = "[]",
                MissingCategoryNames = "[]",
                RecentWindowCompletedAt = now,
            });
            context.CatalogueVideos.Add(new CatalogueVideoRow
            {
                PrdbId = Guid.NewGuid(),
                Title = "Recent",
                NormalisedTitle = "recent",
                CreatedAtUtc = now.AddDays(-1),
                LastReadAt = now - RecentWindow.RevalidateAfter,
            });
            context.Releases.Add(new ReleaseRow
            {
                IndexerId = indexerId,
                DerivedReleaseId = "recent",
                RawGuid = "recent",
                Title = "Recent",
                NormalisedTitle = "recent",
                Categories = "[]",
                PostDate = now.AddDays(-1),
                PubDate = now.AddDays(-1),
                FirstSeenAt = now,
                DownloadUrl = "https://indexer.invalid/get/recent",
                IdentificationState = IdentificationState.Awaiting,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            status.Stages.Single(stage => stage.Id == "sync-prdb").Gaps,
            gap => gap.Title == "Recent Catalogue details are still being prepared");
        Assert.Contains(
            status.Stages.Single(stage => stage.Id == "match").Gaps,
            gap => gap.Title == "Recent Release Identification is still being prepared");
    }

    [Fact]
    public async Task Automatic_non_acts_are_visible_as_decide_brakes_and_facts()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            var indexerId = Guid.NewGuid();
            context.Indexers.Add(new IndexerRow
            {
                Id = indexerId,
                Name = "Fixture",
                Url = "https://indexer.invalid",
                ApiKey = "fixture",
                Categories = "Adult",
                LastVerdict = IndexerConnectionOutcome.Saved,
                LastCheckedAt = database.Time.GetUtcNow(),
            });
            context.Releases.Add(new ReleaseRow
            {
                IndexerId = indexerId,
                DerivedReleaseId = "held",
                RawGuid = "held",
                Title = "held",
                NormalisedTitle = "held",
                Categories = "[]",
                PostDate = database.Time.GetUtcNow(),
                PubDate = database.Time.GetUtcNow(),
                DownloadUrl = "https://indexer.invalid/nzb",
                FirstSeenAt = database.Time.GetUtcNow(),
                IdentificationState = IdentificationState.Matched,
                AutomationDecisionReason = AutomationDecisionReason.AutomaticDownloadCap,
                AutomationPending = true,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);
        var decide = status.Stages.Single(stage => stage.Id == "decide");
        Assert.Contains(decide.Brakes, brake => brake.Title.Contains("automatic Download cap"));
        Assert.Contains(decide.Facts, fact => fact.Label == "Current automatic non-acts"
            && fact.Value.Contains("AutomaticDownloadCap 1"));
    }

    [Fact]
    public async Task Failed_routines_for_one_indexer_are_one_gap()
    {
        await using var database = await TestDatabase.CreateAsync();
        var indexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000711");

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Indexers.Add(new IndexerRow
            {
                Id = indexerId,
                Name = "The Indexer",
                Url = "https://indexer.invalid/api",
                ApiKey = "held only in the test database",
                Categories = "XXX",
                LastVerdict = IndexerConnectionOutcome.Saved,
                LastCheckedAt = database.Time.GetUtcNow(),
            });
            context.Routines.AddRange(
                Routine(DiscoveryRoutineNames.Walk, indexerId, 3),
                Routine(DiscoveryRoutineNames.WantedSweep, indexerId, 4));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, status.GapCount);
        var gap = Assert.Single(status.Stages.Single(stage => stage.Id == "sync-indexers").Gaps);
        // ADR 0058's return journey: a settings route carries where it was
        // followed from, so the mask's back control lands on Status rather than
        // on the settings index.
        Assert.Equal(
            "/settings/connections/indexers/0198ec28-1c00-7000-8000-000000000711?from=/status",
            gap.Route);
        Assert.Contains("Indexer walk", gap.Detail);
        Assert.Contains("Wanted sweep", gap.Detail);
    }

    [Fact]
    public async Task Reporting_switched_off_with_a_difference_is_a_brake_not_a_gap()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Installation.ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.PrdbUserHash, "account")
                    .SetProperty(row => row.ReportConfirmedAssignments, false),
                TestContext.Current.CancellationToken);
            context.ConfirmedAssignments.Add(new ConfirmedAssignmentRow
            {
                OsHash = "hash",
                VideoId = Guid.Parse("0198ec28-1c00-7000-8000-000000000712"),
                UserHash = "account",
                ArrivalFileName = "arrival.mkv",
                ReleaseName = "release",
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.GapCount);
        var brake = Assert.Single(
            status.Stages.Single(stage => stage.Id == "file").Brakes,
            item => item.Title == "Confirmed-assignment reporting is off");
        Assert.Equal("/settings/reporting?from=/status", brake.Route);
    }

    /// <summary>
    /// ADR 0064: the only Brake raised while a channel is switched <em>on</em>.
    /// Nothing is broken — the tool is working exactly as configured and
    /// deliberately not publishing until somebody has read what would leave.
    /// </summary>
    [Fact]
    public async Task Publication_waiting_to_be_explained_is_a_brake_not_a_gap()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.GapCount);
        var brake = Assert.Single(
            status.Stages.Single(stage => stage.Id == "file").Brakes,
            item => item.Title == "Preview publication is waiting to be explained");
        Assert.Equal("/settings/reporting?from=/status", brake.Route);
    }

    /// <summary>And once it has been explained, the Brake is gone.</summary>
    [Fact]
    public async Task An_explained_publication_raises_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<FabDbContext>()
                .Installation.ExecuteUpdateAsync(
                    update => update.SetProperty(
                        row => row.PreviewPublicationExplainedAt,
                        database.Time.GetUtcNow()),
                    TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            status.Stages.SelectMany(stage => stage.Brakes),
            item => item.Title == "Preview publication is waiting to be explained");
    }

    /// <summary>
    /// ADR 0064's uncertain upload, on the page as what it is: a Brake, because
    /// nothing is broken and the tool is deliberately not sending again.
    /// </summary>
    /// <remarks>
    /// The detail is asserted rather than only the title. This is the one state
    /// in the tool that waits for a person because no amount of retrying
    /// settles it, so a page that said only <em>1 upload</em> would leave
    /// somebody to guess why nothing is happening about it.
    /// </remarks>
    [Fact]
    public async Task An_upload_of_unknown_outcome_is_a_brake_not_a_gap()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.PreviewPublications.Add(new PreviewPublicationRow
            {
                Id = Guid.NewGuid(),
                VideoFileId = Guid.NewGuid(),
                VideoPrdbId = Guid.NewGuid(),
                OsHash = "A1B2C3D4E5F60718",
                UserHash = "a-user-hash",
                OutputVersion = PreviewPublicationContract.OutputVersion,
                State = PreviewPublicationState.Uncertain,
                Note = "Nothing came back.",
                IntendedAt = database.Time.GetUtcNow(),
                SettledAt = database.Time.GetUtcNow(),
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.GapCount);

        var brake = Assert.Single(
            status.Stages.Single(stage => stage.Id == "file").Brakes,
            item => item.Title == "Generated previews were submitted and their outcome is unknown");

        Assert.Contains("1 upload(s)", brake.Detail, StringComparison.Ordinal);
        Assert.Contains("not sent", brake.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the other Brake on this channel: the generated queue at its ceiling,
    /// which stops generation until the uploads drain.
    /// </summary>
    [Fact]
    public async Task A_full_generated_queue_is_a_brake()
    {
        await using var database = await TestDatabase.CreateAsync();

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
                    UserHash = "a-user-hash",
                    OutputVersion = PreviewPublicationContract.OutputVersion,
                    State = PreviewPublicationState.Ready,
                    IntendedAt = database.Time.GetUtcNow(),
                    GeneratedAt = database.Time.GetUtcNow(),
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.GapCount);
        Assert.Single(
            status.Stages.Single(stage => stage.Id == "file").Brakes,
            item => item.Title == "The generated preview queue is full");
    }

    /// <summary>
    /// A paused Library request is a Brake: somebody said so, and nothing of it
    /// is given up while it is held.
    /// </summary>
    [Fact]
    public async Task A_paused_library_request_is_a_brake()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            context.PreviewBackfills.Add(new PreviewBackfillRow
            {
                Id = Guid.NewGuid(),
                UserHash = "a-user-hash",
                State = PreviewBackfillState.Paused,
                Selected = 40,
                TakenUp = 7,
                RequestedAt = database.Time.GetUtcNow(),
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.GapCount);

        var brake = Assert.Single(
            status.Stages.Single(stage => stage.Id == "file").Brakes,
            item => item.Title == "The Library preview request is paused");

        Assert.Contains("7 of about 40", brake.Detail, StringComparison.Ordinal);
        Assert.Contains("given up", brake.Detail, StringComparison.Ordinal);

        // And the prdb stage says where the request stands, which is the fact
        // rather than the condition.
        Assert.Contains(
            status.Stages.Single(stage => stage.Id == "sync-prdb").Facts,
            fact => fact.Label == "Library preview request"
                    && fact.Value.Contains("Paused", StringComparison.Ordinal));
    }

    /// <summary>
    /// Switching the channel off with previews already made is a Brake with the
    /// count beside it: ADR 0064 has the switch stop sending rather than drop
    /// what is made, so the rows wait.
    /// </summary>
    [Fact]
    public async Task Publication_switched_off_with_work_in_hand_is_a_brake()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

            await context.Installation.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.PublishGeneratedPreviews, false),
                TestContext.Current.CancellationToken);

            foreach (var state in (PreviewPublicationState[])
                     [PreviewPublicationState.Intended, PreviewPublicationState.Ready])
            {
                context.PreviewPublications.Add(new PreviewPublicationRow
                {
                    Id = Guid.NewGuid(),
                    VideoFileId = Guid.NewGuid(),
                    VideoPrdbId = Guid.NewGuid(),
                    OsHash = state == PreviewPublicationState.Ready
                        ? "A1B2C3D4E5F60718"
                        : "0F0E0D0C0B0A0908",
                    UserHash = "a-user-hash",
                    OutputVersion = PreviewPublicationContract.OutputVersion,
                    State = state,
                    IntendedAt = database.Time.GetUtcNow(),
                });
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = await reading.ServiceProvider.GetRequiredService<StatusService>()
            .ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, status.GapCount);

        var brake = Assert.Single(
            status.Stages.Single(stage => stage.Id == "file").Brakes,
            item => item.Title == "Preview publication is off");

        Assert.Contains("2 generated preview(s)", brake.Detail, StringComparison.Ordinal);
        Assert.Contains("stays at prdb", brake.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_now_refuses_an_empty_work_set_without_changing_due_time()
    {
        await using var database = await TestDatabase.CreateAsync();
        var due = database.Time.GetUtcNow().AddHours(1);
        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            context.Routines.Add(new RoutineRow
            {
                Name = DiscoveryRoutineNames.Screening,
                Lane = Lane.Bulk,
                DueAt = due,
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var status = reading.ServiceProvider.GetRequiredService<StatusService>();
        var verdict = await status.RunNowAsync(
            new StatusRunNowRequest(DiscoveryRoutineNames.Screening, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(RunNowOutcome.Refused, verdict.Outcome);
        var row = await reading.ServiceProvider.GetRequiredService<FabDbContext>().Routines
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(due, row.DueAt);
        Assert.Equal(RunNowOutcome.Refused, row.LastRunNowOutcome);
    }

    private static RoutineRow Routine(string name, Guid indexerId, int failures) => new()
    {
        Name = name,
        Target = indexerId.ToString("D"),
        Lane = Lane.Bulk,
        DueAt = DateTimeOffset.MaxValue,
        ConsecutiveFailures = failures,
    };
}
