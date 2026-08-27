using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Web;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// prdb, pretending, one path at a time.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0042 replaces the network at the socket, so what sits above this is the
/// real SDK, the real handler chain, the real governor and the real routine.
/// What the tests using it are about is what a page does to the database and
/// what the next request asks for, and both of those are only worth anything
/// against the real client: the query strings and conditional headers these
/// assertions read are the ones Kiota built.
/// </para>
/// <para>
/// Hand-written rather than recorded, and answers are queued per path so that a
/// test can say what the second page is. An exhausted queue repeats its last
/// answer, because "ask again and get the same page" is the ordinary state of an
/// idle feed and every test would otherwise have to say so.
/// </para>
/// </remarks>
internal sealed class FakePrdbApi : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Queue<Answer>> queued = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Answer> last = new(StringComparer.Ordinal);
    private readonly List<Asking> asked = [];

    /// <summary>One request, as much of it as an assertion has asked about.</summary>
    /// <param name="Body">
    /// What was sent, for the endpoints where the request rather than the query
    /// string is the interesting half — <c>POST /videos/batch</c> carries the
    /// ids a pass decided to ask about, and nothing else records that decision.
    /// Empty for a request with no body.
    /// </param>
    public sealed record Asking(Uri Uri, string? IfNoneMatch, string Body);

    /// <summary>What prdb answers with once.</summary>
    private sealed record Answer(HttpStatusCode Status, string? Json, string? EntityTag);

    /// <summary>
    /// Every request that was made, whole, newest last. A copy: a lane is a
    /// background worker, so whoever reads this is on another thread from
    /// whoever is adding to it.
    /// </summary>
    public IReadOnlyList<Asking> Requests
    {
        get
        {
            lock (asked)
            {
                return [.. asked];
            }
        }
    }

    public IReadOnlyList<Uri> Asked => [.. Requests.Select(request => request.Uri)];

    /// <summary>
    /// The hourly window every answer reports, or null for a fake that says
    /// nothing about the budget — which leaves the governor with no reading and
    /// therefore sending, exactly as it does before the first response.
    /// </summary>
    public (int Limit, int Remaining, int ResetInSeconds)? Hourly { get; set; }

    /// <summary>Queues one JSON body for <paramref name="path"/>.</summary>
    public FakePrdbApi Answers(string path, string json, string? entityTag = null) =>
        Queue(path, new Answer(HttpStatusCode.OK, json, entityTag));

    /// <summary>
    /// Queues the answer a conditional request gets while nothing has changed:
    /// no body, and the validator that is still current.
    /// </summary>
    public FakePrdbApi AnswersNotModified(string path, string entityTag) =>
        Queue(path, new Answer(HttpStatusCode.NotModified, null, entityTag));

    /// <summary>What was asked of <paramref name="path"/>, in order.</summary>
    public IReadOnlyList<Asking> AskingFor(string path) =>
        [.. Requests.Where(request => request.Uri.AbsolutePath == path)];

    /// <summary>The same, for a test that only cares about the query string.</summary>
    public IReadOnlyList<Uri> AskedFor(string path) =>
        [.. AskingFor(path).Select(request => request.Uri)];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (asked)
        {
            asked.Add(new Asking(
                uri,
                request.Headers.TryGetValues("If-None-Match", out var conditions)
                    ? conditions.FirstOrDefault()
                    : null,
                body));
        }

        var path = uri.AbsolutePath;

        if (RefusedForMissingSince(uri) is { } refusal)
        {
            return refusal;
        }

        if (Next(path) is not { } answer)
        {
            // A path the test never said anything about. Answering 404 rather
            // than an empty page, so that a routine reaching somewhere nobody
            // meant it to shows up as a failure instead of as quiet success.
            return Problem(HttpStatusCode.NotFound, $"Nothing was said about {path}");
        }

        var response = new HttpResponseMessage(answer.Status);

        if (answer.Json is { } json)
        {
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (answer.EntityTag is { } tag)
        {
            response.Headers.TryAddWithoutValidation("ETag", tag);
        }

        if (Hourly is { } window)
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit-Hour", window.Limit.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining-Hour", window.Remaining.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-Hour", window.ResetInSeconds.ToString());
        }

        return response;
    }

    /// <summary>
    /// The rule the service has and its document does not: a change feed
    /// refuses a request with no <c>Since</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written here after a deployment found it. Every one of the five feeds
    /// answered <c>400</c> with
    /// <c>{"errors":{"Since":["The Since field is required."]}}</c>, and no
    /// test refused the request because this fake was built from prdb's
    /// OpenAPI document, which marks the parameter optional. A fake that
    /// answers whatever it is asked is a fake that agrees with the code rather
    /// than with the service.
    /// </para>
    /// <para>
    /// So the fake is stricter than the document too, and the suite states the
    /// service's rule. It applies to the change feeds only: <c>GET /videos</c>
    /// and <c>GET /sites</c> have no such parameter and are unaffected, which
    /// is exactly the split the deployment saw.
    /// </para>
    /// </remarks>
    private static HttpResponseMessage? RefusedForMissingSince(Uri uri)
    {
        if (!uri.AbsolutePath.EndsWith("/changes", StringComparison.Ordinal))
        {
            return null;
        }

        var query = HttpUtility.ParseQueryString(uri.Query);

        return string.IsNullOrEmpty(query["Since"])
            ? Problem(HttpStatusCode.BadRequest, "The Since field is required.")
            : null;
    }

    private FakePrdbApi Queue(string path, Answer answer)
    {
        var answers = queued.GetOrAdd(path, _ => new Queue<Answer>());

        lock (answers)
        {
            answers.Enqueue(answer);
        }

        return this;
    }

    private Answer? Next(string path)
    {
        if (queued.TryGetValue(path, out var answers))
        {
            lock (answers)
            {
                if (answers.Count > 0)
                {
                    return last[path] = answers.Dequeue();
                }
            }
        }

        return last.TryGetValue(path, out var repeated) ? repeated : null;
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string title) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"type":"about:blank","title":"{{title}}","status":{{(int)status}}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
}
