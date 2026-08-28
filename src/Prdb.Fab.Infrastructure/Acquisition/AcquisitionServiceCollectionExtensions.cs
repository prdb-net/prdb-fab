using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Acquisition;

public static class AcquisitionServiceCollectionExtensions
{
    public static IServiceCollection AddFabAcquisition(this IServiceCollection services)
    {
        services.AddScoped<ReleaseRankings>();
        services.AddScoped<PersonDownloads>();
        services.AddScoped<DownloadBrowse>();
        services.AddScoped<IReleasePin, DownloadReleasePin>();
        services.AddScoped<SabnzbdRoutine>();
        services.AddScoped<DownloadFollowingRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<SabnzbdRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<DownloadFollowingRoutine>());
        return services;
    }
}
