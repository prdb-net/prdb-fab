using System.Net;
using System.Text;
using System.Web;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// SABnzbd, pretending. Hand-written, as ADR 0042 says it should be: SABnzbd is
/// one application, and what it does with a key is not in dispute.
/// </summary>
/// <remarks>
/// What it does carry faithfully is the part ADR 0010's rule exists for:
/// <c>version</c> and <c>auth</c> answer without a key, so a check built on
/// either of them would confirm a wrong one. This fake answers both of them to
/// anybody, and every other mode only to the right key — which is what makes
/// "a wrong key is rejected" a claim about the check rather than about the fake.
/// </remarks>
internal sealed class FakeSabnzbd : HttpMessageHandler
{
    public const string RightKey = "3f1c8a2b4d6e0f9a1c3e5b7d9f0a2c4e";

    public const string CompletedFolder = "/downloads/complete";

    /// <summary>
    /// SABnzbd's own categories, and each one's folder. An empty folder is the
    /// ordinary case; an absolute one overrides the completed folder entirely.
    /// </summary>
    public Dictionary<string, string> Categories { get; } = new()
    {
        ["*"] = string.Empty,
        ["xxx"] = "xxx",
        ["archive"] = "/mnt/tank/archive",
    };

    /// <summary>
    /// Whether SABnzbd refuses because of where the request came from rather
    /// than because of the key — which it decides before looking at one.
    /// </summary>
    public bool RefusesThisNetwork { get; set; }

    /// <summary>
    /// What the transport does instead of answering. A refused connection is an
    /// <see cref="HttpRequestException"/>; the wait that ends in ADR 0041's
    /// timeout surfaces as a <see cref="TaskCanceledException"/>, and is raised
    /// rather than waited out — the thirty seconds are the thing being mapped,
    /// not the thing being checked.
    /// </summary>
    public Exception? Throws { get; set; }

    public List<string> Modes { get; } = [];

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
        var mode = query["mode"] ?? string.Empty;
        var key = query["apikey"];

        Modes.Add(mode);

        if (RefusesThisNetwork)
        {
            // Checked before the key is even read, and answered as plain text
            // with a 403 like every other refusal here.
            return Text(HttpStatusCode.Forbidden, "Access denied");
        }

        // The two that answer to anybody, and the reason ADR 0010 refuses to
        // build a check on either.
        if (mode is "version")
        {
            return Json("""{"version":"5.1.1"}""");
        }

        if (mode is "auth")
        {
            return Json("""{"auth":"apikey"}""");
        }

        if (key != RightKey)
        {
            return Text(HttpStatusCode.Forbidden, key is null ? "API Key Required" : "API Key Incorrect");
        }

        return mode switch
        {
            "get_cats" => Json($"{{\"categories\":[{string.Join(',', Categories.Keys.Select(Quoted))}]}}"),

            "fullstatus" => Json(
                "{\"status\":{\"completedir\":" + Quoted(CompletedFolder)
                + ",\"downloaddir\":\"/downloads/incomplete\"}}"),

            "get_config" => Json(
                "{\"config\":{\"categories\":["
                + string.Join(
                    ',',
                    Categories.Select(category =>
                        $"{{\"name\":{Quoted(category.Key)},\"dir\":{Quoted(category.Value)}}}"))
                + "]}}"),

            _ => Json("""{"status":false,"error":"not implemented"}"""),
        };
    }

    private static string Quoted(string value) => $"\"{value}\"";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
}
