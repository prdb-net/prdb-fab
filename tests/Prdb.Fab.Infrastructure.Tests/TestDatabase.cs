using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Xunit;

using Prdb.Fab.Infrastructure.Access;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Scheduling;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

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

    /// <summary>
    /// A migrated database and the services around it.
    /// </summary>
    /// <param name="migratedTo">
    /// Stop at this migration — an older release's schema, so that a test can
    /// migrate it forward the way a started container does. Null for the schema
    /// this build expects.
    /// </param>
    /// <param name="prdb">
    /// Stands at the prdb socket (ADR 0042), so everything above it — the SDK,
    /// the governor's handler, the agent, the timeout — is the real thing.
    /// </param>
    /// <param name="also">
    /// Anything else this test needs registered, which in practice is a routine
    /// for the schedule to turn. Adding one is not replacing one: ADR 0035
    /// forbids swapping a service out to get past the wiring, and a routine
    /// that exists only in a test is a caller rather than a double.
    /// </param>
    public static async Task<TestDatabase> CreateAsync(
        string? migratedTo = null,
        HttpMessageHandler? prdb = null,
        Action<IServiceCollection>? also = null)
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
        services.AddFabReleaseDiscovery();
        services.AddFabAcquisition();
        services.AddFabFiling();

        if (prdb is not null)
        {
            services.AddHttpClient(FabTransports.Prdb).ConfigurePrimaryHttpMessageHandler(() => prdb);
        }

        also?.Invoke(services);

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
