using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// ADR 0014's governor, driven the way it will be driven: a routine in a lane,
/// a real SDK client, and prdb replaced at the socket (ADR 0042).
/// </summary>
public sealed class GovernorTests
{
    /// <summary>
    /// The whole of it: a budget short enough that a user feed is held back
    /// stops the request being sent, and the routine that would have sent it is
    /// neither failed nor recorded. ADR 0018's Brake against its Gap, before
    /// either exists as a surface.
    /// </summary>
    [Fact]
    public async Task A_spent_budget_defers_the_routine_rather_than_failing_it()
    {
        var prdb = new FakePrdb { Hourly = (Limit: 1000, Remaining: 1, ResetInSeconds: 600) };

        await using var database = await CreateAsync(prdb);

        // The first run has nothing to go on and sends: this is how the budget
        // is discovered at all. It comes back saying one request is left.
        await TurnAsync(database);
        Assert.Equal(1, prdb.Requests);

        var deferredAt = database.Time.GetUtcNow();
        await MakeDueAsync(database);
        await TurnAsync(database);

        // Nothing was sent, because the answer to the first one said so.
        Assert.Equal(1, prdb.Requests);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        // One run in the log, from the request that went out. A deferral is not
        // a run.
        var runs = await context.RoutineRuns
            .Where(row => row.Routine!.Name == GovernedRoutine.RoutineName)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Succeeded, Assert.Single(runs).Outcome);

        var routine = await RowAsync(context);

        // And no failure counted, because nothing failed.
        Assert.Equal(0, routine.ConsecutiveFailures);
        Assert.Null(routine.LastFailureAt);

        // It comes back when a slot frees up rather than after its cadence.
        Assert.Equal(deferredAt + TimeSpan.FromSeconds(600), routine.DueAt);
    }

    /// <summary>
    /// ADR 0014: a <c>429</c> carrying a <c>Retry-After</c> overrides the
    /// backoff exactly. Ninety seconds means ninety seconds, and not the
    /// doubled interval a failure would have produced — which for this routine
    /// would be two hours.
    /// </summary>
    [Fact]
    public async Task A_429_waits_exactly_as_long_as_prdb_asked()
    {
        var prdb = new FakePrdb
        {
            Answers = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 90,
        };

        await using var database = await CreateAsync(prdb);

        var refusedAt = database.Time.GetUtcNow();
        await TurnAsync(database);

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();
        var routine = await RowAsync(context);

        Assert.Equal(refusedAt + TimeSpan.FromSeconds(90), routine.DueAt);

        // And it is a brake rather than a break: prdb answered, the plan is
        // simply smaller than the schedule, which ADR 0014 makes a condition of
        // its own rather than a failure.
        Assert.Equal(0, routine.ConsecutiveFailures);
        Assert.Empty(await context.RoutineRuns.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// prdb's fail-closed <c>503</c> is ordinary backoff: three of them are
    /// ADR 0014's Gap, and the tool keeps asking, because a <c>503</c> is
    /// documented as temporary.
    /// </summary>
    [Fact]
    public async Task Three_503s_are_a_gap_and_a_fourth_is_still_attempted()
    {
        var prdb = new FakePrdb { Answers = HttpStatusCode.ServiceUnavailable };

        await using var database = await CreateAsync(prdb);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await MakeDueAsync(database);
            await TurnAsync(database);
        }

        Assert.Equal(3, prdb.Requests);

        await using (var scope = database.Scope())
        {
            var routine = await RowAsync(scope.ServiceProvider.GetRequiredService<FabDbContext>());

            // Three consecutive failures, which is what ADR 0014 raises a Gap
            // on and what ADR 0018's page will read.
            Assert.Equal(3, routine.ConsecutiveFailures);
        }

        await MakeDueAsync(database);
        await TurnAsync(database);

        Assert.Equal(4, prdb.Requests);
    }

    /// <summary>
    /// ADR 0014: a permanent refusal is a Gap at once and stops the routine,
    /// since retrying a settled answer buys nothing and spends the budget
    /// finding that out.
    /// </summary>
    [Fact]
    public async Task A_403_stops_everything_and_says_why()
    {
        var prdb = new FakePrdb { Answers = HttpStatusCode.Forbidden };

        await using var database = await CreateAsync(prdb);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await MakeDueAsync(database);
            await TurnAsync(database);
        }

        // One request, and three deferrals after it.
        Assert.Equal(1, prdb.Requests);

        // The Gap. It cannot be a count of failures, because after the first
        // refusal the routines stop running rather than failing — so it is the
        // governor that holds it, and the status page will read it there.
        var governor = database.Services.GetRequiredService<PrdbGovernor>();
        Assert.Equal(403, governor.RefusedWith);
    }

    /// <summary>
    /// And the way back, which is the reason a refusal is held in the governor
    /// rather than written on the routine: the person fixes the key, ADR 0010's
    /// check goes out under the one kind of request that is never deferred, and
    /// the answer lifts the refusal for everything.
    /// </summary>
    [Fact]
    public async Task A_key_that_works_again_lets_the_routines_go()
    {
        var prdb = new FakePrdb { Answers = HttpStatusCode.Forbidden };

        await using var database = await CreateAsync(prdb);

        await TurnAsync(database);
        Assert.Equal(1, prdb.Requests);

        prdb.Answers = HttpStatusCode.OK;

        await using (var scope = database.Scope())
        {
            var check = await scope.ServiceProvider
                .GetRequiredService<PrdbGateway>()
                .CheckAsync(GovernedRoutine.ApiKey, TestContext.Current.CancellationToken);

            Assert.Equal(Core.Connections.PrdbConnectionOutcome.Saved, check.Outcome);
        }

        Assert.Equal(2, prdb.Requests);
        Assert.Null(database.Services.GetRequiredService<PrdbGovernor>().RefusedWith);

        await MakeDueAsync(database);
        await TurnAsync(database);

        Assert.Equal(3, prdb.Requests);
    }

    private static async Task<TestDatabase> CreateAsync(FakePrdb prdb)
    {
        var database = await TestDatabase.CreateAsync(
            prdb: prdb,
            also: services => services.AddScoped<IRoutine, GovernedRoutine>());

        // The rows, and then ADR 0014's restart spread, which has just given
        // this routine an offset because it is one of two overdue ones. Making
        // it due again is what a test wants and what the spread is not about.
        await database.Services.PrepareFabScheduleAsync(TestContext.Current.CancellationToken);
        await MakeDueAsync(database);

        return database;
    }

    /// <summary>
    /// One turn of the sync lane, through the code the worker uses. The worker
    /// itself is timing and is tested where it lives.
    /// </summary>
    private static async Task TurnAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<RoutineRunner>()
            .TurnAsync(Lane.Sync, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// ADR 0038's <em>run now</em>, used here for what it is: one write that
    /// makes a row due, so a test does not have to wait out a cadence or a
    /// backoff. A forced run passes the governor like any other, which is
    /// exactly what these tests are about.
    /// </summary>
    private static async Task MakeDueAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<IRoutineStore>()
            .RunNowAsync(GovernedRoutine.RoutineName, target: null, TestContext.Current.CancellationToken);
    }

    private static async Task<RoutineRow> RowAsync(FabDbContext context) =>
        await context.Routines.SingleAsync(
            row => row.Name == GovernedRoutine.RoutineName, TestContext.Current.CancellationToken);
}
