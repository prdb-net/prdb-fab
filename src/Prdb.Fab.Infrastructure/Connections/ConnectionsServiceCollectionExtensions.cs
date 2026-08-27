using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Fab.Infrastructure.Connections;

public static class ConnectionsServiceCollectionExtensions
{
    /// <summary>
    /// ADR 0010's four connections: the transports they are reached over, the
    /// gateways that are the only places each service is reached from, and the
    /// four things that write what was checked.
    /// </summary>
    public static IServiceCollection AddFabConnections(this IServiceCollection services)
    {
        services.AddFabTransports();

        services.AddScoped<PrdbGateway>();
        services.AddScoped<SabnzbdGateway>();
        services.AddScoped<NewznabGateway>();

        services.AddScoped<PrdbConnections>();
        services.AddScoped<SabnzbdConnections>();
        services.AddScoped<Indexers>();
        services.AddScoped<LibraryRoots>();

        return services;
    }
}
