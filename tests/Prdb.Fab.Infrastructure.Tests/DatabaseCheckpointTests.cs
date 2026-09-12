using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// ADR 0059: the write-ahead log ADR 0039 opened is folded back and truncated
/// on a schedule, because SQLite's own autocheckpoint gives up whenever a
/// reader is in the way.
/// </summary>
public sealed class DatabaseCheckpointTests
{
    /// <summary>Enough rows that the log is a file rather than a header.</summary>
    private const int Videos = 500;

    [Fact]
    public async Task A_run_empties_the_write_ahead_log()
    {
        await using var database = await TestDatabase.CreateAsync();
        await FillAsync(database);

        Assert.True(LogBytes(database) > 0);

        await using (var scope = database.Scope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<DatabaseCheckpointRoutine>()
                .RunAsync(target: null, TestContext.Current.CancellationToken);

            Assert.Equal(RunOutcome.Succeeded, result.Outcome);
        }

        Assert.Equal(0, LogBytes(database));

        // And the rows it folded back are still there, which is the half of
        // TRUNCATE worth stating: it is a move rather than a discard.
        await using var reading = database.Scope();

        Assert.Equal(
            Videos,
            await reading.ServiceProvider.GetRequiredService<FabDbContext>().CatalogueVideos
                .CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// ADR 0038: an ordinary routine row of its own, in the bulk lane, so that
    /// nothing waits on it and the row is the only truth about when it is next
    /// due.
    /// </summary>
    [Fact]
    public async Task The_routine_is_a_bulk_lane_row()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var registering = database.Scope())
        {
            await registering.ServiceProvider.GetRequiredService<RoutineRegistrar>()
                .EnsureRowsExistAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = database.Scope();

        var lane = await scope.ServiceProvider.GetRequiredService<FabDbContext>().Routines
            .Where(row => row.Name == DatabaseCheckpointRoutine.RoutineName)
            .Select(row => row.Lane)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Lane.Bulk, lane);
    }

    private static long LogBytes(TestDatabase database)
    {
        var log = new FileInfo(database.Location.FilePath + "-wal");

        return log.Exists ? log.Length : 0;
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
}
