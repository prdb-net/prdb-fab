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
}
