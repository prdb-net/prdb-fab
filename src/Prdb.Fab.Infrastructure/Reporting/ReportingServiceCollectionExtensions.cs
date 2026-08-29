using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Reporting;

public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddFabReporting(this IServiceCollection services)
    {
        services.AddScoped<FulfilmentDifference>();
        services.AddScoped<ReportingSettings>();
        services.AddScoped<ReportingRoutine>();
        services.AddScoped<IRoutine>(provider => provider.GetRequiredService<ReportingRoutine>());

        return services;
    }
}
