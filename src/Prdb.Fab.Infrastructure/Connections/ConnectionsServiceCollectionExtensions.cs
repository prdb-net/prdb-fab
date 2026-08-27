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
        // ADR 0014's governor is one for the process: what it holds is one
        // account's hourly window, and every routine spends from the same one.
        // Registered before the transports, because the handler that asks it is
        // part of the prdb transport's chain.
        services.AddSingleton<PrdbGovernor>();
        services.AddTransient<PrdbGovernorHandler>();

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
