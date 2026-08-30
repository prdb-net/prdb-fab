using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Migrations;

/// <summary>Forward migration of product scaffolding that no longer ships.</summary>
public sealed class RetirementMigrationTests
{
    private const string BeforeRetirement = "EnableLeftoverDeletionByDefault";
    private const string BeforeRecentWindow = "ManualSearchWorkspace";

    [Fact]
    public async Task The_walking_skeleton_table_routine_and_run_are_removed()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: BeforeRetirement);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            // This database deliberately has the old shape. Insert through
            // that contract rather than through today's entity, whose newer
            // Status columns cannot exist until the final migration below.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO routine (Name, Target, Lane, DueAt, ConsecutiveFailures) VALUES ('skeleton-sweep', NULL, 'Bulk', '2026-08-29 12:00:00', 0)",
                TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO routine_run (RoutineId, StartedAt, FinishedAt, Outcome, ItemsHandled) SELECT Id, '2026-08-29 12:00:00', '2026-08-29 12:00:00', 'Succeeded', 1 FROM routine WHERE Name = 'skeleton-sweep'",
                TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO skeleton_item (Label, AddedAt) VALUES ('retire me', '2026-08-29 12:00:00')",
                TestContext.Current.CancellationToken);

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var migrated = reading.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.DoesNotContain(
            await migrated.Routines.ToListAsync(TestContext.Current.CancellationToken),
            routine => routine.Name == "skeleton-sweep");
        Assert.Empty(await migrated.RoutineRuns.ToListAsync(TestContext.Current.CancellationToken));

        await migrated.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = migrated.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'skeleton_item'";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task An_existing_installation_gains_recent_window_state_without_losing_releases()
    {
        await using var database = await TestDatabase.CreateAsync(migratedTo: BeforeRecentWindow);
        var indexerId = Guid.Parse("0198ec28-1c00-7000-8000-000000000901");
        var at = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        const string indexerName = "A test indexer";
        const string indexerUrl = "https://indexer.invalid/api";
        const string releaseUrl = "https://indexer.invalid/get/kept";
        const string key = "key";
        const string adult = "Adult";
        const string saved = "Saved";
        const string emptyJson = "[]";
        const string kept = "kept";
        const string keptTitle = "Kept";
        const string unknown = "Unknown";

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO indexer
                    (Id, Name, Url, ApiKey, Categories, LastVerdict, LastCheckedAt,
                     Enabled, Rank, DailyQueryBudget)
                VALUES
                    ({indexerId}, {indexerName}, {indexerUrl}, {key},
                     {adult}, {saved}, {at}, {true}, {0}, {1000})
                """, TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO indexer_walk_state
                    (IndexerId, CapsTree, ResolvedCategoryIds, MissingCategoryNames,
                     QueryDay, QueriesSpentToday, SweepQueriesSpentToday, BootstrapCompletedAt)
                VALUES
                    ({indexerId}, {emptyJson}, {emptyJson}, {emptyJson}, {at}, {0}, {0}, {at})
                """, TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO release
                    (IndexerId, DerivedReleaseId, RawGuid, Title, NormalisedTitle,
                     Categories, PostDate, PubDate, DownloadUrl, FirstSeenAt,
                     IdentificationState, SearchWasReason, AutomationPending)
                VALUES
                    ({indexerId}, {kept}, {kept}, {keptTitle}, {kept}, {emptyJson},
                     {at}, {at}, {releaseUrl}, {at},
                     {unknown}, {false}, {false})
                """, TestContext.Current.CancellationToken);

            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.Scope();
        var migrated = reading.ServiceProvider.GetRequiredService<FabDbContext>();
        Assert.Equal("kept", (await migrated.Releases.SingleAsync(
            TestContext.Current.CancellationToken)).DerivedReleaseId);
        Assert.Null((await migrated.Releases.SingleAsync(
            TestContext.Current.CancellationToken)).LastIdentifiedAt);

        var indexerState = await migrated.IndexerWalkStates.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(0, indexerState.RecentWindowResumePage);
        Assert.Null(indexerState.RecentWindowCompletedAt);
        Assert.Equal(RecentWindowStateRow.TheOnlyRow, (await migrated.RecentWindowState.SingleAsync(
            TestContext.Current.CancellationToken)).Id);

        await migrated.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = migrated.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('indexer_walk_state') WHERE name = 'BootstrapCompletedAt'";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }
}
