using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Prdb.Fab.Host.Tests.Access;

/// <summary>
/// ADR 0010 spends a paragraph on how narrow the surface in front of the
/// password is: <em>there are exactly two writes anyone may make without being
/// signed in</em>, both gated on the same single condition, and that is
/// <em>the only unauthenticated write path in the application — which makes it
/// the one to test.</em>
/// </summary>
/// <remarks>
/// So this walks the routing table rather than a list of routes somebody
/// remembered to check. Everything is behind the password by default
/// (<c>Program.cs</c> sets a fallback policy), and each exception is written
/// down here with the reason it is one. Adding an anonymous endpoint fails this
/// test, which is the point: it should cost an argument.
/// </remarks>
public sealed class AnonymousSurfaceTests
{
    /// <summary>
    /// The whole of it. Restore joins as the second write, on the same
    /// condition, when ADR 0009's file exists.
    /// </summary>
    private static readonly string[] Expected =
    [
        // Says the process is answering, and nothing else. What the image's own
        // smoke test asks.
        "GET /api/health",

        // The first of ADR 0010's two unauthenticated writes.
        "POST /api/access/password",

        // Not a write against the installation: it mints a session for whoever
        // already knows the password, and is throttled.
        "POST /api/access/sign-in",

        // The page that shows the sign-in form. ADR 0036: routing happens in
        // the browser. Static files are served by middleware rather than by an
        // endpoint, so this one row covers the whole frontend.
        "GET {*path:nonfile}",
        "HEAD {*path:nonfile}",
    ];

    [Fact]
    public void The_anonymous_surface_is_exactly_what_was_argued_for()
    {
        using var application = new FabApplication();

        var endpoints = application.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var anonymous = endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                    .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Expected.Order(StringComparer.Ordinal), anonymous);
    }
}
