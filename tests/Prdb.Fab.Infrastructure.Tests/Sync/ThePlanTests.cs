using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0014's named condition: a plan too small for the schedule is a
/// degradation in a documented order and a Gap, rather than a governor
/// deferring everything forever while nothing ever fails.
/// </summary>
public sealed class ThePlanTests
{
    /// <summary>
    /// The number every later routine is judged against, asserted against the
    /// routines actually registered rather than written down twice. A routine
    /// added later with a cadence that moves it fails here — which is the point
    /// of the test, and the reason the profile is a constant at all.
    /// </summary>
    [Fact]
    public async Task The_idle_profile_is_the_cadences_of_the_routines_that_spend_on_a_clock()
    {
        await using var database = await TestDatabase.CreateAsync(
            also: services => services.AddFabSync());

        await using var scope = database.Scope();

        var onAClock = scope.ServiceProvider
            .GetServices<IRoutine>()
            // The repair pass is steered by the budget, the bootstraps retire,
            // and the artwork routine spends nothing at prdb — so none of them
            // is one of these, and what is left is ADR 0014's table.
            .OfType<ISpendsPrdbBudget>()
            .Where(routine => routine is not IOneShot)
            .Cast<IRoutine>()
            .ToList();

        Assert.Equal(7, onAClock.Count);

        var anHour = onAClock.Sum(routine => 1 / routine.Cadence.TotalHours);

        Assert.Equal(IdleProfile.RequestsAnHour, anHour, precision: 6);

        // And it really is about nine, which is the sentence ADR 0014 uses.
        Assert.InRange(anHour, 9, 10);
    }

    /// <summary>
    /// Six requests an hour against a schedule that wants nine. The routine is
    /// shed to ADR 0014's daily, and the condition is written down.
    /// </summary>
    [Fact]
    public async Task A_plan_too_small_sheds_in_the_documented_order()
    {
        var prdb = new FakePrdb { Hourly = (Limit: 6, Remaining: 6, ResetInSeconds: 600) };

        await using var database = await CreateAsync(prdb);

        var at = database.Time.GetUtcNow();

        // The first turn has nothing to go on and sends, which is how the limit
        // is discovered at all; what it learns is applied when the run is
        // recorded, so the row it leaves behind is already shed.
        await TurnAsync(database);

        var row = await RowAsync(database);

        Assert.Equal(at + TimeSpan.FromHours(24), row.DueAt);
        Assert.Equal(1, prdb.Requests);
    }

    /// <summary>
    /// The Gap, recorded once. A condition re-recorded every tick is a stamp
    /// that always says <em>now</em>, when how long it has been true is the one
    /// thing worth knowing about it.
    /// </summary>
    [Fact]
    public async Task The_condition_is_recorded_once_rather_than_every_run()
    {
        var prdb = new FakePrdb { Hourly = (Limit: 6, Remaining: 6, ResetInSeconds: 600) };

        await using var database = await CreateAsync(prdb);

        await TurnAsync(database);

        // The first turn discovered the limit; the second is the one that finds
        // the answer changed and writes it.
        await TurnAsync(database);

        var recorded = await PlanShortSinceAsync(database);

        Assert.NotNull(recorded);

        database.Time.Advance(TimeSpan.FromHours(2));

        await TurnAsync(database);
        await TurnAsync(database);

        Assert.Equal(recorded, await PlanShortSinceAsync(database));
    }

    /// <summary>
    /// A plan that carries the profile. Nothing is shed and nothing is
    /// recorded — the ordinary state, and it has to be silent or the condition
    /// says nothing when it does arrive.
    /// </summary>
    [Fact]
    public async Task A_plan_that_carries_the_schedule_sheds_nothing_and_records_nothing()
    {
        var prdb = new FakePrdb { Hourly = (Limit: 1000, Remaining: 900, ResetInSeconds: 600) };

        await using var database = await CreateAsync(prdb);

        var at = database.Time.GetUtcNow();

        await TurnAsync(database);
        await TurnAsync(database);

        var row = await RowAsync(database);

        // The routine's own cadence, untouched.
        Assert.Equal(at + TimeSpan.FromHours(6), row.DueAt);

        Assert.Null(await PlanShortSinceAsync(database));
    }

    /// <summary>
    /// The limit is read off prdb's own answers, so a plan raised at prdb lifts
    /// the shedding on the next answer that arrives. Nothing is restarted and
    /// nothing is told.
    /// </summary>
    [Fact]
    public async Task Raising_the_limit_lifts_the_shedding_without_a_restart()
    {
        var prdb = new FakePrdb { Hourly = (Limit: 6, Remaining: 6, ResetInSeconds: 600) };

        await using var database = await CreateAsync(prdb);

        await TurnAsync(database);
        await TurnAsync(database);

        Assert.NotNull(await PlanShortSinceAsync(database));

        // The plan is upgraded. The next answer says so.
        prdb.Hourly = (Limit: 1000, Remaining: 900, ResetInSeconds: 600);

        database.Time.Advance(TimeSpan.FromHours(1));

        await MakeDueAsync(database);

        var at = database.Time.GetUtcNow();

        await TurnAsync(database);
        await TurnAsync(database);

        Assert.Null(await PlanShortSinceAsync(database));

        var row = await RowAsync(database);

        Assert.Equal(at + TimeSpan.FromHours(6), row.DueAt);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdb prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddScoped<IRoutine, ShedRoutine>());

        await database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);
        await MakeDueAsync(database);

        return database;
    }

    private static async Task TurnAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<RoutineRunner>()
            .TurnAsync(Lane.Sync, TestContext.Current.CancellationToken);
    }

    private static async Task MakeDueAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<IRoutineStore>()
            .RunNowAsync(ShedRoutine.RoutineName, target: null, TestContext.Current.CancellationToken);
    }

    private static async Task<RoutineRow> RowAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .Routines
            .SingleAsync(row => row.Name == ShedRoutine.RoutineName, TestContext.Current.CancellationToken);
    }

    private static async Task<DateTimeOffset?> PlanShortSinceAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider
            .GetRequiredService<FabDbContext>()
            .Installation
            .Select(row => row.PlanShortSince)
            .SingleAsync(TestContext.Current.CancellationToken);
    }
}
