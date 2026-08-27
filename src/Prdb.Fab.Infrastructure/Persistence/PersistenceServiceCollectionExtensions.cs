using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Fab.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database in <paramref name="dataDirectory"/> — the mounted
    /// volume ADR 0034 provisions.
    /// </summary>
    public static IServiceCollection AddFabPersistence(
        this IServiceCollection services,
        string dataDirectory)
    {
        var location = new FabDatabaseLocation(dataDirectory);

        services.AddSingleton(location);
        services.AddSingleton<SqlitePragmaInterceptor>();

        // ADR 0039: short-lived contexts, and reads that do not track. A lane
        // holds no context across a run — ADR 0004's rule that no transaction
        // spans a call is what keeps SQLite's single writer from ever being
        // contended, and it was priced at 2 735 ms when broken.
        services.AddDbContext<FabDbContext>((provider, options) => options
            .UseSqlite(location.ConnectionString)
            .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddScoped<DatabaseMigrator>();

        return services;
    }

    /// <summary>
    /// Applies the migrations. Called at startup, before anything is served and
    /// before the lanes turn; throws <see cref="DatabaseMigrationException"/>
    /// when the database cannot be brought up to date, which stops the process.
    /// </summary>
    public static async Task PrepareFabDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<DatabaseMigrator>()
            .PrepareAsync(cancellationToken);
    }
}
