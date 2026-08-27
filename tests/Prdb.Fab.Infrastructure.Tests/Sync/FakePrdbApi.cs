using System.Collections.Concurrent;
using System.Net;
using System.Text;

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
/// against the real client: the query string these assertions read is the one
/// Kiota built.
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
    private readonly ConcurrentDictionary<string, Queue<string>> queued = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> last = new(StringComparer.Ordinal);

    /// <summary>Every request that was made, whole, newest last.</summary>
    public List<Uri> Asked { get; } = [];

    /// <summary>
    /// The hourly window every answer reports, or null for a fake that says
    /// nothing about the budget — which leaves the governor with no reading and
    /// therefore sending, exactly as it does before the first response.
    /// </summary>
    public (int Limit, int Remaining, int ResetInSeconds)? Hourly { get; set; }

    /// <summary>Queues one JSON body for <paramref name="path"/>.</summary>
    public FakePrdbApi Answers(string path, string json)
    {
        queued.GetOrAdd(path, _ => new Queue<string>()).Enqueue(json);

        return this;
    }

    /// <summary>What was asked of <paramref name="path"/>, in order.</summary>
    public IReadOnlyList<Uri> AskedFor(string path) =>
        Asked.Where(uri => uri.AbsolutePath == path).ToList();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        lock (Asked)
        {
            Asked.Add(uri);
        }

        var path = uri.AbsolutePath;
        var body = Next(path);

        if (body is null)
        {
            // A path the test never said anything about. Answering 404 rather
            // than an empty page, so that a routine reaching somewhere nobody
            // meant it to shows up as a failure instead of as quiet success.
            return Task.FromResult(Problem(HttpStatusCode.NotFound, path));
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (Hourly is { } window)
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit-Hour", window.Limit.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining-Hour", window.Remaining.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset-Hour", window.ResetInSeconds.ToString());
        }

        return Task.FromResult(response);
    }

    private string? Next(string path)
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

    private static HttpResponseMessage Problem(HttpStatusCode status, string path) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"type":"about:blank","title":"Nothing was said about {{path}}","status":{{(int)status}}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
}
