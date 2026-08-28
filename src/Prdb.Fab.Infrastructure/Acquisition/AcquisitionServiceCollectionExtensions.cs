using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Acquisition;

public static class AcquisitionServiceCollectionExtensions
{
    public static IServiceCollection AddFabAcquisition(this IServiceCollection services)
    {
        services.AddScoped<ReleaseRankings>();
        services.AddScoped<PersonDownloads>();
        services.AddScoped<SabnzbdRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<SabnzbdRoutine>());
        return services;
    }
}
