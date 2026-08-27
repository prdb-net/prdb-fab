using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Xunit;

using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;

namespace Prdb.Fab.Infrastructure.Tests;

/// <summary>
/// A real SQLite file in a real temporary directory, migrated the way the
/// application migrates it.
/// </summary>
/// <remarks>
/// ADR 0042: real SQLite rather than the in-memory provider, because what these
/// tests are about — the pragmas, the migration, what a unique index refuses —
/// is precisely what the in-memory provider does not have.
/// </remarks>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly string directory;

    /// <summary>
    /// Kept here rather than read off <see cref="Location"/> at the end,
    /// because by then the provider that holds it has been disposed.
    /// </summary>
    private readonly string connectionString;

    private TestDatabase(ServiceProvider provider, string directory, FakeTimeProvider time)
    {
        this.provider = provider;
        this.directory = directory;
        connectionString = provider.GetRequiredService<FabDatabaseLocation>().ConnectionString;
        Time = time;
    }

    /// <summary>ADR 0042: the tests move the clock rather than waiting for it.</summary>
    public FakeTimeProvider Time { get; }

    public IServiceProvider Services => provider;

    public FabDatabaseLocation Location => provider.GetRequiredService<FabDatabaseLocation>();

    public static Task<TestDatabase> CreateAsync() => CreateAsync(migratedTo: null);

    /// <summary>
    /// The same, stopped at <paramref name="migratedTo"/> — an older release's
    /// schema, so that a test can migrate it forward the way a started
    /// container does.
    /// </summary>
    public static async Task<TestDatabase> CreateAsync(string? migratedTo)
    {
        var directory = Path.Combine(Path.GetTempPath(), "prdb-fab-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddLogging();
        services.AddFabPersistence(directory);
        services.AddFabScheduling();
        services.AddFabAccess();
        services.AddFabConnections();

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<FabDbContext>().Database;

            if (migratedTo is null)
            {
                await database.MigrateAsync(TestContext.Current.CancellationToken);
            }
            else
            {
                await database.GetService<IMigrator>()
                    .MigrateAsync(migratedTo, TestContext.Current.CancellationToken);
            }
        }

        return new TestDatabase(provider, directory, time);
    }

    public AsyncServiceScope Scope() => provider.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();

        // The pooled connections have to be gone before the file can be
        // removed on every platform. This pool only: clearing every pool in the
        // process reaches into whatever else is running beside this test, and
        // one of the things running beside it asserts on what a pool hands back.
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(
            new Microsoft.Data.Sqlite.SqliteConnection(connectionString));

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
