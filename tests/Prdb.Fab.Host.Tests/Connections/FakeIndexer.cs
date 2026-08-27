using System.Net;
using System.Text;
using System.Web;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// A Newznab-style indexer, answering from the recorded documents under
/// <c>Recorded/</c>. ADR 0042's one departure: these are recorded rather than
/// hand-written, because the research surveyed five implementations that
/// disagree and a hand-written fixture would encode the assumption the code
/// already shares.
/// </summary>
/// <remarks>
/// The behaviour that matters most is the one this fake insists on:
/// <c>t=caps</c> is answered <em>without</em> a key, exactly as three of the
/// four open implementations do. So an indexer check that leaned on caps would
/// pass here with any key at all, which is what makes the search a real test.
/// </remarks>
internal sealed class FakeIndexer : HttpMessageHandler
{
    public const string RightKey = "9b3d1f5a7c2e4068b1d3f5a7c9e1b3d5";

    /// <summary>Which recorded capabilities document this indexer answers with.</summary>
    public string Caps { get; set; } = "caps.xml";

    /// <summary>What a search answers instead of the feed, when it does.</summary>
    public HttpStatusCode SearchStatus { get; set; } = HttpStatusCode.OK;

    public string? SearchBody { get; set; }

    /// <summary>
    /// What the transport does instead of answering. A refused connection is an
    /// <see cref="HttpRequestException"/>; the wait that ends in ADR 0041's
    /// timeout surfaces as a <see cref="TaskCanceledException"/>, and is raised
    /// rather than waited out — the thirty seconds are the thing being mapped,
    /// not the thing being checked.
    /// </summary>
    public Exception? Throws { get; set; }

    public List<string> Functions { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(Answer(request));

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        if (Throws is { } refusing)
        {
            throw refusing;
        }

        var query = HttpUtility.ParseQueryString(request.RequestUri?.Query ?? string.Empty);
        var function = query["t"] ?? string.Empty;

        Functions.Add(function);

        // No key check here, deliberately. It is what the real ones do.
        if (function is "caps")
        {
            return Xml(HttpStatusCode.OK, Recorded(Caps));
        }

        if (function is not "search")
        {
            return Xml(HttpStatusCode.OK, """<?xml version="1.0"?><error code="202" description="No such function"/>""");
        }

        if (SearchBody is { } body)
        {
            return Xml(SearchStatus, body);
        }

        return query["apikey"] == RightKey
            ? Xml(HttpStatusCode.OK, Recorded("search.xml"))
            : Xml(HttpStatusCode.OK, Recorded("unauthorized.xml"));
    }

    public static string Recorded(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Connections", "Recorded", name));

    private static HttpResponseMessage Xml(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };
}
