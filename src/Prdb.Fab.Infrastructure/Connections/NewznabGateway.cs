using System.Globalization;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The one place an indexer is reached from. One transport for all of them —
/// ADR 0041: a client is a transport, not an address, and the URL travels with
/// the request.
/// </summary>
public sealed class NewznabGateway(IHttpClientFactory clients, ILogger<NewznabGateway> logger)
{
    /// <summary>
    /// ADR 0010's check for an indexer, in the order the two calls have to
    /// happen in: a real search, and then the category tree.
    /// </summary>
    /// <remarks>
    /// The search is first because it is the only one of the two that proves
    /// anything about the key. <c>t=caps</c> is not a key test — three of the
    /// four implementations the research surveyed answer it without a key at
    /// all — and it is read second, for the one part of a capabilities document
    /// that is worth trusting.
    /// </remarks>
    public async Task<NewznabCheck> CheckAsync(
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            return NewznabCheck.Refusing(IndexerConnectionOutcome.NotAnIndexer);
        }

        var client = clients.CreateClient(FabTransports.Indexers);

        var search = await ReadAsync(client, address, apiKey, "search&limit=1", cancellationToken);
        if (search.Refusal is { } refusedSearch)
        {
            return NewznabCheck.Refusing(refusedSearch, search.Said);
        }

        if (search.Document?.Root?.Name.LocalName is not "rss")
        {
            // A search that answers with neither a feed nor an error document is
            // not something to guess about.
            return NewznabCheck.Refusing(IndexerConnectionOutcome.NotAnIndexer);
        }

        // Sent with the key even though the spec does not require one here.
        // Both clients surveyed do the same, and a server that has decided
        // otherwise costs nothing to humour.
        var caps = await ReadAsync(client, address, apiKey, "caps", cancellationToken);
        if (caps.Refusal is { } refusedCaps)
        {
            return NewznabCheck.Refusing(refusedCaps, caps.Said);
        }

        var tree = CategoriesIn(caps.Document);

        logger.LogInformation(
            "The indexer at {Host} answered a search and offered {Count} top-level categories.",
            address.Host,
            tree.Count);

        return new NewznabCheck(IndexerConnectionOutcome.Saved, Said: null, tree);
    }

    /// <summary>
    /// The category tree out of a capabilities document.
    /// </summary>
    /// <remarks>
    /// Deliberately more careful than the two clients the research read, both of
    /// which do <c>Attribute("name").Value</c> with no null check and throw on a
    /// server that leaves one out. A category with no name or no id is skipped
    /// rather than fatal, because the tree is the one thing in a capabilities
    /// document worth having.
    /// </remarks>
    private static IReadOnlyList<CapsCategory> CategoriesIn(XDocument? document)
    {
        var categories = document?.Root?.Element("categories")?.Elements("category");

        if (categories is null)
        {
            return [];
        }

        return
        [
            .. categories
                .Select(category => Node(
                    category,
                    [.. category.Elements("subcat").Select(sub => Node(sub, [])).OfType<CapsCategory>()]))
                .OfType<CapsCategory>(),
        ];
    }

    private static CapsCategory? Node(XElement element, IReadOnlyList<CapsCategory> children)
    {
        var name = element.Attribute("name")?.Value;
        var id = element.Attribute("id")?.Value;

        if (string.IsNullOrWhiteSpace(name)
            || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return new CapsCategory(number, name, children);
    }

    private async Task<NewznabDocument> ReadAsync(
        HttpClient client,
        Uri address,
        string? apiKey,
        string function,
        CancellationToken cancellationToken)
    {
        // The key is a query parameter because Newznab has no header form. That
        // is also what puts it in every download URL an indexer hands back,
        // which ADR 0037 leaned on and ADR 0041 turned into the redirect rule
        // this transport is registered with.
        var request = With(address, $"t={function}&apikey={Uri.EscapeDataString(apiKey ?? string.Empty)}");

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(request, cancellationToken);
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "The indexer at {Host} did not answer: {Reason}.",
                address.Host,
                unreachable.GetType().Name);

            return NewznabDocument.Refusing(IndexerConnectionOutcome.NotRightNow);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;

            XDocument document;

            try
            {
                document = XDocument.Parse(body);
            }
            catch (System.Xml.XmlException)
            {
                // An HTML page is what a blocked address, a login wall and a
                // reverse proxy answering for a service that is not there all
                // look like. Both clients surveyed say the same about it.
                return NewznabDocument.Refusing(
                    status is >= 500 and <= 599
                        ? IndexerConnectionOutcome.NotRightNow
                        : IndexerConnectionOutcome.NotAnIndexer);
            }

            if (document.Root?.Name.LocalName == "error")
            {
                var described = document.Root.Attribute("description")?.Value;
                var code = int.TryParse(
                    document.Root.Attribute("code")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var number)
                    ? number
                    : (int?)null;

                var outcome = IndexerConnection.ForError(status, code, described);

                logger.LogInformation(
                    "The indexer at {Host} refused: code {Code} at HTTP {Status}, read as {Outcome}.",
                    address.Host,
                    code,
                    status,
                    outcome);

                return NewznabDocument.Refusing(outcome, described);
            }

            // The spec claims a protocol error always arrives as HTTP 200 and is
            // wrong about the implementation it names by name: two of the five
            // surveyed map their error codes onto real statuses. So the body
            // decides first, and the status only matters when there was no
            // document to read.
            return response.IsSuccessStatusCode
                ? new NewznabDocument(null, null, document)
                : NewznabDocument.Refusing(IndexerConnectionOutcome.NotRightNow);
        }
    }

    private static Uri With(Uri address, string query)
    {
        var builder = new UriBuilder(address);
        var existing = builder.Query.TrimStart('?');

        builder.Query = existing.Length > 0 ? $"{existing}&{query}" : query;

        return builder.Uri;
    }

    private sealed record NewznabDocument(
        IndexerConnectionOutcome? Refusal,
        string? Said,
        XDocument? Document)
    {
        public static NewznabDocument Refusing(IndexerConnectionOutcome outcome, string? said = null) =>
            new(outcome, said, null);
    }
}

/// <summary>
/// What an indexer answered. <see cref="IndexerConnectionOutcome.Saved"/> here
/// means it answered a real search; whether a row is written is the caller's.
/// </summary>
/// <param name="Said">The indexer's own wording, when it refused in its own words.</param>
public sealed record NewznabCheck(
    IndexerConnectionOutcome Outcome,
    string? Said,
    IReadOnlyList<CapsCategory> Categories)
{
    public static NewznabCheck Refusing(IndexerConnectionOutcome outcome, string? said = null) =>
        new(outcome, said, []);
}
