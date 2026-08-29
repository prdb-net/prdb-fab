using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Prdb.Fab.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Fab.Host.Tests;

/// <summary>
/// Starting against a data directory an older release left behind, which is the
/// only way anybody who is already running this gets a new schema.
/// </summary>
/// <remarks>
/// ADR 0039 runs the migrations before the listener and before the lanes, and
/// ADR 0044 stops the process rather than serving against a schema it does not
/// understand — so an answered request is the whole assertion: it cannot have
/// been answered by a tool that failed to migrate.
/// </remarks>
public sealed class UpgradeTests
{
    /// <summary>The last migration 0.1.0 shipped.</summary>
    private const string TheFirstRelease = "TheGapsASkipLeavesBehind";

    [Fact]
    public async Task The_application_starts_on_a_data_directory_from_the_first_release()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "prdb-fab-host-tests", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(directory);

        var location = new FabDatabaseLocation(directory);

        await using (var context = new FabDbContext(
            new DbContextOptionsBuilder<FabDbContext>().UseSqlite(location.ConnectionString).Options))
        {
            await context.Database.GetService<IMigrator>()
                .MigrateAsync(TheFirstRelease, TestContext.Current.CancellationToken);
        }

        await using var application = FabApplication.On(directory);

        using var client = application.CreateClient();
        using var health = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        health.EnsureSuccessStatusCode();

        await using var connection = new SqliteConnection(location.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM catalogue_video;";

        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        command.CommandText = "SELECT DeleteLeftovers FROM installation;";
        Assert.Equal(1L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
