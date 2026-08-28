using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public static class ReleaseDiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddFabReleaseDiscovery(this IServiceCollection services)
    {
        services.AddScoped<IndexerSearch>();
        Routine<IndexerCapsRoutine>(services);
        Routine<IndexerWalkRoutine>(services);
        Routine<IndexerBootstrapRoutine>(services);
        Routine<IndexerCatchUpRoutine>(services);
        return services;
    }

    public static async Task PrepareFabReleaseDiscoveryAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DiscoveryState>()
            .EnsureFoundationAsync(cancellationToken);
    }

    private static void Routine<TRoutine>(IServiceCollection services)
        where TRoutine : class, IRoutine
    {
        services.AddScoped<TRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<TRoutine>());
    }
}
