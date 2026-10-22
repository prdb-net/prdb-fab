using System.Net;
using System.Text;

// Shared socket fixture for tests concerned with domain behavior rather than
// admission. It models the unmetered account endpoint and absent quota headers.
// Dedicated Governor fixtures use their own explicit counters instead.
internal sealed class BudgetSocket(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly Dictionary<string, string> last = new()
    {
        ["Limit-Hour"] = "1000000", ["Remaining-Hour"] = "999999", ["Reset-Hour"] = "1",
        ["Limit-Month"] = "1000000000", ["Remaining-Month"] = "999999999", ["Reset-Month"] = "1",
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/rate-limit")
        {
            string Window(string suffix) => $$"""{"limit":{{last["Limit-" + suffix]}},"remaining":{{last["Remaining-" + suffix]}},"resetsInSeconds":{{last["Reset-" + suffix]}}}""";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"hourly\":" + Window("Hour") + ",\"monthly\":" + Window("Month") + "}", Encoding.UTF8, "application/json"),
            };
        }
        var response = await base.SendAsync(request, cancellationToken);
        foreach (var key in last.Keys.ToArray())
        {
            if (response.Headers.TryGetValues("X-RateLimit-" + key, out var values)) last[key] = values.First();
            else response.Headers.TryAddWithoutValidation("X-RateLimit-" + key, last[key]);
        }
        return response;
    }
}
