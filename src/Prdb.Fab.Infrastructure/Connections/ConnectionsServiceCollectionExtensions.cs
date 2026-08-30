using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Fab.Infrastructure.Connections;

using Prdb.Fab.Infrastructure.ReleaseDiscovery;

public static class ConnectionsServiceCollectionExtensions
{
    /// <summary>
    /// ADR 0010's four connections: the transports they are reached over, the
    /// gateways that are the only places each service is reached from, and the
    /// four things that write what was checked.
    /// </summary>
    public static IServiceCollection AddFabConnections(
        this IServiceCollection services,
        string? prdbBaseUrl = null)
    {
        // Production deliberately uses the SDK's canonical origin. A local
        // development host may supply one of the SDK's narrowly accepted
        // loopback HTTP origins without turning that address into an
        // installation setting or a second source of truth for the API key.
        services.AddSingleton(new PrdbEndpoint(
            prdbBaseUrl ?? PrdbEndpoint.Production));

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

        // The fourth remote thing, and the one that is not a connection the user
        // configures: ADR 0030 fetches an image from whatever host prdb named in
        // its payload.
        services.AddScoped<ArtworkGateway>();

        services.AddScoped<PrdbConnections>();
        services.AddScoped<SabnzbdConnections>();
        services.AddScoped<Indexers>();
        services.AddScoped<LibraryRoots>();
        services.AddScoped<DiscoveryState>();
        services.AddScoped<ReleaseRows>();

        return services;
    }
}
