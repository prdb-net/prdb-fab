using System.Net;
using System.Text;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// prdb, pretending, with its rate-limit headers under the test's control.
/// </summary>
/// <remarks>
/// ADR 0042 replaces the network at the socket, so what sits above this is the
/// real SDK, the real handler chain and the real governor. Hand-written rather
/// than recorded because the whole point is answers a real server would only
/// give after a thousand requests.
/// </remarks>
internal sealed class FakePrdb : HttpMessageHandler
{
    public HttpStatusCode Answers { get; set; } = HttpStatusCode.OK;

    /// <summary>What a <c>429</c> asks for, when it asks for anything.</summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// The hourly window this answer reports, or null for a response prdb did
    /// not meter — which is what a <c>401</c>, a <c>403</c> and a <c>503</c>
    /// really are.
    /// </summary>
    public (int Limit, int Remaining, int ResetInSeconds)? Hourly { get; set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests++;

        var response = Answers is HttpStatusCode.OK
            ? Json(HttpStatusCode.OK, """{"userHash":"5b1f0c2e9a7d4f3b8c6e1a0d2f4b6a8c","activeSubscriptions":[]}""", "application/json")
            : Json(
                Answers,
                $$"""{"type":"about:blank","title":"{{Answers}}","status":{{(int)Answers}}}""",
                "application/problem+json");

        if (Hourly is { } window)
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit-Hour", window.Limit.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining-Hour", window.Remaining.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-Hour", window.ResetInSeconds.ToString());
        }

        if (RetryAfterSeconds is { } seconds)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", seconds.ToString());
        }

        return Task.FromResult(response);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string contentType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
}
