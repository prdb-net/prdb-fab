using System.Net;
using System.Text;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// prdb, pretending. One server whose behaviour is known, so ADR 0042 has this
/// hand-written rather than recorded — and it stands at the socket, so the SDK,
/// the timeout, the redirect rule and the mapping from a status code to a
/// sentence a person reads all run for real above it.
/// </summary>
internal sealed class FakePrdb : HttpMessageHandler
{
    public const string OneAccount = "5b1f0c2e9a7d4f3b8c6e1a0d2f4b6a8c";

    public const string AnotherAccount = "0a9c8b7d6e5f4a3b2c1d0e9f8a7b6c5d";

    /// <summary>What the next request is answered with.</summary>
    public HttpStatusCode Answers { get; set; } = HttpStatusCode.OK;

    /// <summary>Which prdb account the key that arrives belongs to.</summary>
    public string UserHash { get; set; } = OneAccount;

    /// <summary>What a <c>429</c> asks for, when it asks for anything.</summary>
    public int? RetryAfterSeconds { get; set; }

    /// <summary>
    /// What the transport does instead of answering. A refused connection is an
    /// <see cref="HttpRequestException"/>; the wait that ends in ADR 0041's
    /// timeout surfaces as a <see cref="TaskCanceledException"/>, and is raised
    /// rather than waited out — the thirty seconds are the thing being mapped,
    /// not the thing being checked.
    /// </summary>
    public Exception? Throws { get; set; }

    /// <summary>The key the last request carried, in the header prdb expects it in.</summary>
    public string? LastKey { get; private set; }

    /// <summary>What the last request said it was.</summary>
    public string? LastUserAgent { get; private set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Answer(request));

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        Requests++;
        LastKey = request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.FirstOrDefault() : null;
        LastUserAgent = request.Headers.UserAgent.ToString();

        if (Throws is { } refusing)
        {
            throw refusing;
        }

        if (Answers is HttpStatusCode.OK)
        {
            return Json(
                HttpStatusCode.OK,
                $$"""{"userHash":"{{UserHash}}","activeSubscriptions":[]}""",
                "application/json");
        }

        var response = Json(
            Answers,
            $$"""
              {"type":"https://tools.ietf.org/html/rfc9110","title":"{{Answers}}",
               "status":{{(int)Answers}},"detail":"Recorded."}
              """,
            "application/problem+json");

        if (RetryAfterSeconds is { } seconds)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", seconds.ToString());
        }

        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string contentType) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
}
