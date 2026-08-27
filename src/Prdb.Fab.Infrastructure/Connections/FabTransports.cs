using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0041's named transports: one per kind of remote thing, never one per
/// address. A client is a transport, and the URL travels with the request —
/// which is why twenty indexers share one of these.
/// </summary>
public static class FabTransports
{
    public const string Prdb = "prdb";

    public const string Indexers = "indexers";

    public const string Sabnzbd = "sabnzbd";

    /// <summary>
    /// ADR 0030's artwork cache: a <c>GET</c> against a CDN for a URL prdb
    /// handed out in its own payload. Not the prdb transport, which is the whole
    /// point — it carries no key, spends no rate limit, and therefore must not
    /// pass through the governor's handler.
    /// </summary>
    public const string Artwork = "artwork";

    /// <summary>
    /// ADR 0041: the timeout follows the cadence rather than taste. SABnzbd is
    /// polled every five seconds while anything is outstanding, so a longer wait
    /// than this is a poll queued behind a poll.
    /// </summary>
    public static readonly TimeSpan SabnzbdTimeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan PrdbTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan IndexerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// ADR 0041's rule applied to ADR 0030's display path: a page request may
    /// now do network I/O, and this is what keeps it from being a page request
    /// that hangs. A slow CDN serves the no-artwork tile instead, which costs a
    /// thumbnail; a grid held open for thirty seconds costs the page.
    /// </summary>
    public static readonly TimeSpan ArtworkTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// ADR 0041: an honest, identifiable <c>User-Agent</c> of product name and
    /// version, on every transport.
    /// </summary>
    /// <remarks>
    /// Not a formality. The Newznab research found an implementation that ships
    /// middleware blocking specific agent strings outright, and a tool that
    /// downloads unattended against somebody else's service should be legible to
    /// whoever runs it. The version is what makes a complaint about it
    /// actionable. No contact URL, because <c>VISION.md</c> carries no
    /// operational address and a dead link is worse than none.
    /// </remarks>
    public static string UserAgent { get; } = BuildUserAgent();

    /// <summary>
    /// The four transports, one per kind of remote thing.
    /// </summary>
    public static IServiceCollection AddFabTransports(this IServiceCollection services)
    {
        Register(services, Prdb, PrdbTimeout)
            // ADR 0041 puts ADR 0014's governor here, on the transport, rather
            // than at each call site. Added last, so it is the outermost of the
            // handlers and a request it turns away is turned away before
            // anything else has touched it.
            .AddHttpMessageHandler<PrdbGovernorHandler>();

        Register(services, Indexers, IndexerTimeout);
        Register(services, Sabnzbd, SabnzbdTimeout);

        // The fourth, and the one exception to the rule above it. ADR 0041:
        // only the CDN follows a redirect — this request carries no credential
        // of any kind, and an image URL that redirects to the bytes is how a
        // content delivery network ordinarily answers.
        Register(services, Artwork, ArtworkTimeout, followsRedirects: true);

        return services;
    }

    private static IHttpClientBuilder Register(
        IServiceCollection services,
        string name,
        TimeSpan timeout,
        bool followsRedirects = false) =>
        services.AddHttpClient(name, client =>
            {
                client.Timeout = timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            })
            // ADR 0041: none of the three credentialled transports follows a
            // redirect, because all three carry a credential. For prdb the SDK
            // refuses to build on a transport that does; for an indexer it is
            // sharper still, since Newznab puts the key in the query string and
            // a followed redirect hands the whole URL to whatever host it names.
            .ConfigurePrimaryHttpMessageHandler(
                () => new SocketsHttpHandler { AllowAutoRedirect = followsRedirects })
            // The prdb transport is reached as a bare handler rather than as an
            // HttpClient, because the SDK builds the client above it — so the
            // agent is set here, where both paths pass through, rather than on
            // the client where only one of them would see it.
            .AddHttpMessageHandler(() => new UserAgentHandler(UserAgent));

    private static string BuildUserAgent()
    {
        var version = typeof(FabTransports).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        // The build metadata after a plus sign is for a log line, not for
        // somebody else's access log.
        var plus = version.IndexOf('+', StringComparison.Ordinal);

        return $"prdb-fab/{(plus < 0 ? version : version[..plus])}";
    }
}

/// <summary>
/// Puts <see cref="FabTransports.UserAgent"/> at the front of every request's
/// <c>User-Agent</c>, keeping whatever was already there behind it.
/// </summary>
/// <remarks>
/// Rewritten rather than only added to, because on the prdb transport the SDK's
/// own middleware has already put <c>kiota-dotnet</c> there by the time this
/// runs. ADR 0041 wants the tool legible to whoever runs the service being
/// called, and a header led by the name of a code generator is not that. What
/// was there is kept after it: it is true, and it tells the same person which
/// client library the requests are coming through.
/// </remarks>
internal sealed class UserAgentHandler(string userAgent) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var alreadyThere = request.Headers.UserAgent.ToArray();

        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd(userAgent);

        foreach (var product in alreadyThere)
        {
            request.Headers.UserAgent.Add(product);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
