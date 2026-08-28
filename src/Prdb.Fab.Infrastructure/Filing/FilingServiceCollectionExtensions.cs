using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Filing;

public static class FilingServiceCollectionExtensions
{
    public static IServiceCollection AddFabFiling(this IServiceCollection services)
    {
        services.TryAddScoped<IProbeProcess, FfprobeProcess>();
        services.TryAddScoped<ArtworkStore>();
        services.AddScoped<VideoProbe>();
        services.AddScoped<IdentificationSettings>();
        services.AddScoped<CollectingRoutine>();
        services.AddScoped<ArrivalIdentificationRoutine>();
        services.AddScoped<EntryFiles>();
        services.AddScoped<VideoFileMover>();
        services.AddScoped<FilingRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<CollectingRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<ArrivalIdentificationRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<FilingRoutine>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, LibraryEntryVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, DownloadVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, ArrivingFileVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, ArrivingFileCandidateVideoPin>());
        return services;
    }
}
