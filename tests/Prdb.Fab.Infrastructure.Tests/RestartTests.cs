using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// ADR 0014's restart spread, over rows rather than over the arithmetic.
/// </summary>
public sealed class RestartTests
{
    private const string RoutineName = "restart-test";

    /// <summary>
    /// What a container coming back from an update actually looks like: every
    /// routine in the table has been overdue for as long as it was down, so on
    /// the first tick every one of them is due at once. Firing all of them at
    /// prdb and at every indexer in the same second is the shape a rate limit
    /// is least forgiving of.
    /// </summary>
    [Fact]
    public async Task Ten_overdue_routines_do_not_come_back_in_the_same_second()
    {
        await using var database = await TestDatabase.CreateAsync();

        await AddOverdueAsync(database, Lane.Sync, count: 10);

        await SpreadAsync(database);

        var due = await DueTimesAsync(database, Lane.Sync);

        Assert.Equal(10, due.Count);
        Assert.Equal(
            due.Select(at => at.ToUnixTimeSeconds()).Distinct(),
            due.Select(at => at.ToUnixTimeSeconds()));

        // And nothing was pushed past the window it was spread across.
        Assert.All(due, at => Assert.True(at <= database.Time.GetUtcNow() + RestartSpread.Widest));
    }

    /// <summary>
    /// ADR 0014: the live lane is exempt and starts immediately, because a
    /// download in flight has to be picked up at once — and nothing in that
    /// lane leaves the container, so there is nothing to be gentle with.
    /// </summary>
    [Fact]
    public async Task The_live_lane_starts_at_once()
    {
        await using var database = await TestDatabase.CreateAsync();

        await AddOverdueAsync(database, Lane.Live, count: 5);
        await AddOverdueAsync(database, Lane.Bulk, count: 5);

        var restartedAt = database.Time.GetUtcNow();
        await SpreadAsync(database);

        Assert.All(
            await DueTimesAsync(database, Lane.Live),
            at => Assert.Equal(restartedAt - TimeSpan.FromHours(1), at));

        // The bulk ones were moved, which is what says the live ones were left
        // alone rather than simply not reached.
        Assert.Contains(await DueTimesAsync(database, Lane.Bulk), at => at > restartedAt);
    }

    /// <summary>
    /// One routine is already spread, and moving it would make a restart slower
    /// than no spread at all for the only thing there is to run.
    /// </summary>
    [Fact]
    public async Task One_overdue_routine_is_left_where_it_is()
    {
        await using var database = await TestDatabase.CreateAsync();

        await AddOverdueAsync(database, Lane.Bulk, count: 1);

        var wasDueAt = (await DueTimesAsync(database, Lane.Bulk)).Single();
        await SpreadAsync(database);

        Assert.Equal(wasDueAt, (await DueTimesAsync(database, Lane.Bulk)).Single());
    }

    /// <summary>
    /// Rows for a routine the build does have, one per target — which is what
    /// the indexer walk and the wanted sweep are, and the reason ten overdue
    /// routines is an ordinary number rather than a contrived one.
    /// </summary>
    private static async Task AddOverdueAsync(TestDatabase database, Lane lane, int count)
    {
        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FabDbContext>();

        for (var index = 0; index < count; index++)
        {
            context.Routines.Add(new RoutineRow
            {
                Name = RoutineName,
                Target = $"{lane}-{index}",
                Lane = lane,

                // Down for an hour, which is what makes every one of them due
                // on the first tick after coming back.
                DueAt = database.Time.GetUtcNow() - TimeSpan.FromHours(1),
            });
        }

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SpreadAsync(TestDatabase database)
    {
        await using var scope = database.Scope();

        await scope.ServiceProvider
            .GetRequiredService<RoutineRegistrar>()
            .SpreadOverdueAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<DateTimeOffset>> DueTimesAsync(TestDatabase database, Lane lane)
    {
        await using var scope = database.Scope();

        return await scope.ServiceProvider.GetRequiredService<FabDbContext>()
            .Routines
            .Where(row => row.Lane == lane && row.Target != null)
            .OrderBy(row => row.Id)
            .Select(row => row.DueAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
