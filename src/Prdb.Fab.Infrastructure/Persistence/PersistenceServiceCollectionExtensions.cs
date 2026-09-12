using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;

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

        // ADR 0056: the statistics the startup PRAGMA optimize writes once,
        // renewed on the schedule. An ordinary routine under ADR 0038 — one
        // row, one lane, its own due time — and it lives here because what it
        // maintains is the database rather than anything above it.
        services.AddScoped<DatabaseAnalysisRoutine>();
        services.AddScoped<IRoutine>(provider =>
            provider.GetRequiredService<DatabaseAnalysisRoutine>());

        // ADR 0059: the write-ahead log ADR 0039 opened, folded back and
        // truncated on the schedule, because SQLite's own autocheckpoint gives
        // up whenever a reader is in the way and in this tool one usually is.
        services.AddScoped<DatabaseCheckpointRoutine>();
        services.AddScoped<IRoutine>(provider =>
            provider.GetRequiredService<DatabaseCheckpointRoutine>());

        // ADR 0033's account cut, read off the model rather than kept in step
        // by hand. See AccountScopedRows.
        services.AddScoped<AccountScopedRows>();

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
