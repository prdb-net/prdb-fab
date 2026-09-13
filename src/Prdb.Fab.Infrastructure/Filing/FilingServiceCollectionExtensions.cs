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
        services.TryAddScoped<IContactSheetProcess, FfmpegContactSheetProcess>();
        services.TryAddScoped<ArtworkStore>();

        // ADR 0061's interest register, which Filing writes to when a Video
        // File lands. Taken here as well as in AddFabSync for the reason
        // ArtworkStore is: a slice asks for what it uses, and whichever of the
        // two is added first wins.
        services.TryAddScoped<UserPreviews>();
        services.AddScoped<VideoProbe>();
        services.AddScoped<IdentificationSettings>();
        services.AddScoped<LibrarySettings>();
        services.AddScoped<ReviewQueue>();
        services.AddScoped<ReviewFileContactSheet>();
        services.AddScoped<ReviewVideoSearch>();
        services.AddScoped<ReviewDecisions>();
        services.AddScoped<LibraryBrowse>();
        services.AddScoped<LibraryEntryDeletion>();
        services.AddScoped<OperationLogBrowse>();
        services.AddScoped<CollectingRoutine>();
        services.AddScoped<ArrivalIdentificationRoutine>();
        services.AddScoped<EntryFiles>();
        services.AddScoped<VideoFileMover>();
        services.AddScoped<FilingRoutine>();
        services.AddScoped<TidyUpRoutine>();
        services.AddScoped<LibraryVerificationRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<CollectingRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<ArrivalIdentificationRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<FilingRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<TidyUpRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<LibraryVerificationRoutine>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, LibraryEntryVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, DownloadVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, ArrivingFileVideoPin>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICataloguePin, ArrivingFileCandidateVideoPin>());
        return services;
    }
}
