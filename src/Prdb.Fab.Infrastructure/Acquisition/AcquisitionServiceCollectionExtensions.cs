using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Acquisition;

public static class AcquisitionServiceCollectionExtensions
{
    public static IServiceCollection AddFabAcquisition(this IServiceCollection services)
    {
        services.AddScoped<ReleaseRankings>();
        services.AddScoped<DownloadSettings>();
        services.AddScoped<AccountPreferences>();
        services.AddScoped<PersonDownloads>();
        services.AddScoped<DownloadSubmissionRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<DownloadSubmissionRoutine>());
        services.AddScoped<DownloadBrowse>();
        services.AddScoped<DownloadOrigins>();
        services.AddScoped<IReleasePin, DownloadReleasePin>();
        services.AddScoped<SabnzbdRoutine>();
        services.AddScoped<DownloadFollowingRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<SabnzbdRoutine>());
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<DownloadFollowingRoutine>());
        return services;
    }
}
