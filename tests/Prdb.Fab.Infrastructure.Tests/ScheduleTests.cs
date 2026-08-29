using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// The schedule, against a real database and a clock the test moves.
/// </summary>
public sealed class ScheduleTests
{
    private const int ItemsPerRun = 20;
    private const string RoutineName = "schedule-test";

    [Fact]
    public async Task A_routine_gets_a_row_and_is_due_at_once()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        await using var scope = database.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IRoutineStore>();

        var due = await store.DueAsync(Lane.Bulk, TestContext.Current.CancellationToken);

        Assert.Equal(RoutineName, Assert.Single(due, row => row.Name == RoutineName).Name);
    }

    /// <summary>
    /// ADR 0038: a routine belongs to one lane, and another lane does not see
    /// it. Worth asserting because a lane that quietly picked up somebody
    /// else's work would look exactly like one that was busy.
    /// </summary>
    [Fact]
    public async Task Another_lane_does_not_see_it()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        await using var scope = database.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IRoutineStore>();

        Assert.DoesNotContain(
            await store.DueAsync(Lane.Live, TestContext.Current.CancellationToken),
            row => row.Name == RoutineName);
    }

    /// <summary>
    /// ADR 0032, the whole point: the sweep has an empty work set, so it was
    /// never due, so nothing is written to the run log. A tick that did nothing
    /// leaves no trace at all.
    /// </summary>
    [Fact]
    public async Task An_empty_tick_records_nothing_but_still_moves_the_due_time()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        var before = database.Time.GetUtcNow();
        await TurnAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Empty(await context.RoutineRuns.ToListAsync(TestContext.Current.CancellationToken));

        var routine = await context.Routines.SingleAsync(
            row => row.Name == RoutineName,
            TestContext.Current.CancellationToken);
        Assert.True(routine.DueAt > before, "an empty tick still moves the due time, or the lane spins");
        Assert.Equal(0, routine.ConsecutiveFailures);
        Assert.Null(routine.LastSuccessAt);
    }

    /// <summary>
    /// And the other half: a work set that is not empty is a run, and it is
    /// recorded with what it got through.
    /// </summary>
    [Fact]
    public async Task Work_in_the_set_is_a_recorded_run()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        await using (var scope = database.Scope())
        {
            scope.ServiceProvider.GetRequiredService<ScheduleWork>().Add(2);
        }

        await TurnAsync(database);

        await using var reading = database.Scope();
        var context = reading.ServiceProvider.GetRequiredService<FabDbContext>();

        var run = await context.RoutineRuns.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Equal(2, run.ItemsHandled);

        var routine = await context.Routines.SingleAsync(
            row => row.Name == RoutineName,
            TestContext.Current.CancellationToken);
        Assert.Equal(database.Time.GetUtcNow(), routine.LastSuccessAt);

        // And the work is gone from the set, so the next turn is an empty tick.
        Assert.Equal(0, reading.ServiceProvider.GetRequiredService<ScheduleWork>().Count);
    }

    /// <summary>
    /// ADR 0014: a run is bounded, so a long backlog is many short runs rather
    /// than one that holds the lane for as long as the backlog is.
    /// </summary>
    [Fact]
    public async Task A_run_is_bounded_and_the_rest_waits_for_the_next_one()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        await using (var scope = database.Scope())
        {
            scope.ServiceProvider.GetRequiredService<ScheduleWork>().Add(ItemsPerRun + 5);
        }

        await TurnAsync(database);

        await using var reading = database.Scope();
        var context = reading.ServiceProvider.GetRequiredService<FabDbContext>();

        var run = await context.RoutineRuns.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ItemsPerRun, run.ItemsHandled);

        Assert.Equal(5, reading.ServiceProvider.GetRequiredService<ScheduleWork>().Count);
    }

    /// <summary>
    /// ADR 0038: <em>run now</em> is one write to the row, and nothing else. The
    /// routine is not called, no second path exists, and the lane finds it on
    /// its next tick like anything else.
    /// </summary>
    [Fact]
    public async Task Run_now_only_makes_the_row_due()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        await TurnAsync(database);
        database.Time.Advance(TimeSpan.FromSeconds(1));

        await using var scope = database.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IRoutineStore>();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.DoesNotContain(
            await store.DueAsync(Lane.Bulk, TestContext.Current.CancellationToken),
            row => row.Name == RoutineName);

        Assert.True(await store.RunNowAsync(
            RoutineName, target: null, TestContext.Current.CancellationToken));

        Assert.Single(
            await store.DueAsync(Lane.Bulk, TestContext.Current.CancellationToken),
            row => row.Name == RoutineName);

        // Still nothing in the log: making something due is not running it.
        Assert.Empty(await context.RoutineRuns.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0014: fifty runs per routine, so the log stays something a person
    /// reads rather than something that grows for as long as the container runs.
    /// </summary>
    [Fact]
    public async Task The_run_log_keeps_fifty_runs()
    {
        await using var database = await CreateAsync();
        await RegisterAsync(database);

        for (var turn = 0; turn < RoutineStore.RunsKeptPerRoutine + 10; turn++)
        {
            await using (var scope = database.Scope())
            {
                scope.ServiceProvider.GetRequiredService<ScheduleWork>().Add(1);
            }

            await TurnAsync(database);
            database.Time.Advance(TimeSpan.FromSeconds(20));
        }

        await using var reading = database.Scope();
        var context = reading.ServiceProvider.GetRequiredService<FabDbContext>();

        Assert.Equal(
            RoutineStore.RunsKeptPerRoutine,
            await context.RoutineRuns.CountAsync(TestContext.Current.CancellationToken));
    }

    private static async Task RegisterAsync(TestDatabase database)
    {
        await using var scope = database.Scope();
        await scope.ServiceProvider.GetRequiredService<RoutineRegistrar>()
            .EnsureRowsExistAsync(TestContext.Current.CancellationToken);
    }

    private static Task<TestDatabase> CreateAsync() =>
        TestDatabase.CreateAsync(also: services =>
        {
            services.AddSingleton<ScheduleWork>();
            services.AddScoped<IRoutine>(provider =>
                new ScheduleRoutine(provider.GetRequiredService<ScheduleWork>()));
        });

    /// <summary>
    /// One turn of a lane, without the worker: ask what is due, run it, record
    /// it. The worker is tested where it lives, in the host.
    /// </summary>
    private static async Task TurnAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        var store = scope.ServiceProvider.GetRequiredService<IRoutineStore>();
        var routines = scope.ServiceProvider.GetServices<IRoutine>().ToDictionary(routine => routine.Name);

        foreach (var row in (await store.DueAsync(Lane.Bulk, TestContext.Current.CancellationToken))
            .Where(row => row.Name == RoutineName))
        {
            var routine = routines[row.Name];
            var result = await routine.RunAsync(row.Target, TestContext.Current.CancellationToken);

            await store.RecordAsync(
                row.Id, result, routine.Cadence, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ScheduleWork
    {
        public int Count { get; private set; }

        public void Add(int count) => Count += count;

        public int Take(int count)
        {
            var taken = Math.Min(count, Count);
            Count -= taken;
            return taken;
        }
    }

    private sealed class ScheduleRoutine(ScheduleWork work) : IRoutine
    {
        public string Name => RoutineName;

        public Lane Lane => Lane.Bulk;

        public TimeSpan Cadence => TimeSpan.FromSeconds(15);

        public Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
        {
            var handled = work.Take(ItemsPerRun);

            return Task.FromResult(handled == 0 ? RunResult.NothingToDo : RunResult.Handled(handled));
        }
    }
}
