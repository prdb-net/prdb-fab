using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Fab.Infrastructure.Backup;

public static class BackupServiceCollectionExtensions
{
    public static IServiceCollection AddFabBackup(this IServiceCollection services)
    {
        services.AddScoped<Backups>();
        services.AddScoped<Restores>();

        return services;
    }
}
