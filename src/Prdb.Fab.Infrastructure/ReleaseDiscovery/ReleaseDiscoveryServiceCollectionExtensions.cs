using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public static class ReleaseDiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddFabReleaseDiscovery(this IServiceCollection services)
    {
        services.AddScoped<IndexerSearch>();
        services.TryAddScoped<CatalogueRows>();
        services.TryAddScoped<VideoDetails>();
        services.TryAddScoped<CataloguePins>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, WantedVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, ReleaseCandidateVideoPin>());
        services.AddScoped<IReleasePin, WantedIdentificationReleasePin>();
        services.AddScoped<ReleasePins>();
        services.AddScoped<ReleaseEviction>();
        services.AddScoped<ReleaseBrowse>();
        services.AddScoped<ReleaseDiscoveryControls>();
        Routine<IndexerCapsRoutine>(services);
        Routine<IndexerWalkRoutine>(services);
        Routine<IndexerBootstrapRoutine>(services);
        Routine<IndexerCatchUpRoutine>(services);
        Routine<ScreeningRoutine>(services);
        Routine<BackwardsScreeningRoutine>(services);
        Routine<ReleaseIdentificationRoutine>(services);
        Routine<WantedSweepRoutine>(services);
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
