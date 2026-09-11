using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// ADR 0056: the query planner is told what is in the database, once at start
/// and again on the schedule.
/// </summary>
public sealed class DatabaseAnalysisTests
{
    /// <summary>
    /// How many Catalogue Videos the fixture writes. Enough that ANALYZE has
    /// something to record; the ADR's measurements are at a million, and what
    /// this asserts is that it happened rather than how long it took.
    /// </summary>
    private const int Videos = 200;

    [Fact]
    public async Task A_database_that_has_never_been_analysed_carries_statistics_after_one_start()
    {
        await using var database = await TestDatabase.CreateAsync();
        await FillAsync(database);

        Assert.Empty(await StatisticsAsync(database));

        await StartAsync(database);

        Assert.Contains(await StatisticsAsync(database), table => table == "catalogue_video");
    }

    /// <summary>
    /// The second start is the one the ADR priced at 0.0 ms. Asked in SQLite's
    /// own debug mode — the default optimizations plus <c>0x00001</c>, which
    /// reports what would be run instead of running it — so the assertion is
    /// about the work proposed rather than about a duration a test cannot hold
    /// still.
    /// </summary>
    [Fact]
    public async Task A_second_start_proposes_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await FillAsync(database);

        Assert.NotEmpty(await ProposedAsync(database));

        await StartAsync(database);

        Assert.Empty(await ProposedAsync(database));
    }

    /// <summary>
    /// A fresh installation has empty tables, ANALYZE records nothing for them,
    /// and the start after the data arrives writes what the start before it
    /// could not. ADR 0056 leans on this: there is no first-run moment for
    /// anything to detect.
    /// </summary>
    [Fact]
    public async Task An_empty_table_records_nothing_and_the_next_start_repairs_that()
    {
        await using var database = await TestDatabase.CreateAsync();

        await StartAsync(database);

        Assert.DoesNotContain(await StatisticsAsync(database), table => table == "catalogue_video");

        await FillAsync(database);
        await StartAsync(database);

        Assert.Contains(await StatisticsAsync(database), table => table == "catalogue_video");
    }

    /// <summary>
    /// The routine renews what the startup call wrote once. Checked by throwing
    /// the statistics away and watching the run put them back, because a full
    /// ANALYZE over unchanged rows records the same numbers and is otherwise
    /// indistinguishable from having done nothing.
    /// </summary>
    [Fact]
    public async Task The_routine_renews_the_statistics()
    {
        await using var database = await TestDatabase.CreateAsync();
        await FillAsync(database);
        await StartAsync(database);

        await ExecuteAsync(database, "DELETE FROM sqlite_stat1;");
        Assert.Empty(await StatisticsAsync(database));

        await using (var scope = database.Scope())
        {
            var routine = scope.ServiceProvider.GetRequiredService<DatabaseAnalysisRoutine>();
            var result = await routine.RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        }

        Assert.Contains(await StatisticsAsync(database), table => table == "catalogue_video");
    }

    /// <summary>
    /// ADR 0038: an ordinary routine row, in the bulk lane, and the row is the
    /// only truth about when it is next due. So a restart neither runs it again
    /// nor moves it — which for a monthly routine is the difference between
    /// monthly and once per container update.
    /// </summary>
    [Fact]
    public async Task The_routine_is_a_bulk_lane_row_whose_due_time_survives_a_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        await RegisterAsync(database);

        var row = Assert.Single(
            await DueAsync(database, Lane.Bulk),
            due => due.Name == DatabaseAnalysisRoutine.RoutineName);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IRoutineStore>().RecordAsync(
                row.Id,
                RunResult.Handled(1),
                TimeSpan.FromDays(30),
                TestContext.Current.CancellationToken);
        }

        var dueAt = await DueAtAsync(database);

        // The restart: the registrar runs again over a table that already has
        // the row, and the spread passes over everything that is not overdue.
        await RegisterAsync(database);

        Assert.Equal(dueAt, await DueAtAsync(database));
        Assert.DoesNotContain(
            await DueAsync(database, Lane.Bulk),
            due => due.Name == DatabaseAnalysisRoutine.RoutineName);
    }

    /// <summary>
    /// ADR 0056's recorded trap: <c>analysis_limit</c> is per-connection, and a
    /// pooled connection is handed on carrying it. The rule that nothing sets it
    /// is in the architecture tests; this is the same claim read off the pool
    /// once the start and the routine have both run.
    /// </summary>
    [Fact]
    public async Task Neither_the_start_nor_the_routine_leaves_an_analysis_limit_behind()
    {
        await using var database = await TestDatabase.CreateAsync();
        await FillAsync(database);
        await StartAsync(database);

        await using (var scope = database.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseAnalysisRoutine>()
                .RunAsync(null, TestContext.Current.CancellationToken);
        }

        await using var borrowed = new SqliteConnection(database.Location.ConnectionString);
        await borrowed.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = borrowed.CreateCommand();
        command.CommandText = "PRAGMA analysis_limit;";

        Assert.Equal(0L, Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));
    }

    /// <summary>One start, the way the container starts.</summary>
    private static async Task StartAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>()
            .PrepareAsync(TestContext.Current.CancellationToken);
    }

    private static async Task RegisterAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        var registrar = scope.ServiceProvider.GetRequiredService<RoutineRegistrar>();

        await registrar.EnsureRowsExistAsync(TestContext.Current.CancellationToken);
        await registrar.SpreadOverdueAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<DueRoutine>> DueAsync(TestDatabase database, Lane lane)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<IRoutineStore>()
            .DueAsync(lane, TestContext.Current.CancellationToken);
    }

    private static async Task<DateTimeOffset> DueAtAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>().Routines
            .Where(row => row.Name == DatabaseAnalysisRoutine.RoutineName)
            .Select(row => row.DueAt)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task FillAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        context.CatalogueVideos.AddRange(Enumerable.Range(1, Videos)
            .Select(index => new CatalogueVideoRow
            {
                PrdbId = Guid.Parse($"aaaaaaaa-0000-4000-8000-{index:D12}"),
                Title = $"Video {index}",
                NormalisedTitle = $"video {index}",
            }));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The tables SQLite has recorded anything about.</summary>
    private static async Task<IReadOnlyList<string>> StatisticsAsync(TestDatabase database) =>
        await ReadAsync(
            database,
            "SELECT tbl FROM sqlite_stat1;",
            // The statistics table does not exist until something writes it.
            emptyWhenMissing: true);

    /// <summary>
    /// What <c>PRAGMA optimize</c> would do, without doing it: the default mask
    /// <c>0x0fffe</c> with SQLite's debug bit <c>0x00001</c> added.
    /// </summary>
    private static Task<IReadOnlyList<string>> ProposedAsync(TestDatabase database) =>
        ReadAsync(database, "PRAGMA optimize(0x0ffff);", emptyWhenMissing: false);

    private static async Task<IReadOnlyList<string>> ReadAsync(
        TestDatabase database,
        string sql,
        bool emptyWhenMissing)
    {
        await using var connection = new SqliteConnection(database.Location.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();

        try
        {
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add(reader.GetString(0));
            }
        }
        catch (SqliteException) when (emptyWhenMissing)
        {
            return [];
        }

        return rows;
    }

    private static async Task ExecuteAsync(TestDatabase database, string sql)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<FabDbContext>().Database
            .ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
    }
}
