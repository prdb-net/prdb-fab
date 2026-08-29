using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>The sole HTTP and parsing boundary for every Newznab read.</summary>
public sealed partial class NewznabGateway(
    IHttpClientFactory clients,
    TimeProvider time,
    ILogger<NewznabGateway> logger)
{
    public const int PageSize = 100;

    public async Task<NewznabCheck> CheckAsync(
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (!Address(url, out var address))
        {
            return NewznabCheck.Refusing(IndexerConnectionOutcome.NotAnIndexer);
        }

        var search = await ReadAsync(address!, apiKey, SearchQuery([], 0, null, null, 1), cancellationToken);
        if (search.Refusal is { } searchRefusal)
        {
            return NewznabCheck.Refusing(searchRefusal, search.Said);
        }

        if (search.Document?.Root?.Name.LocalName is not "rss")
        {
            return NewznabCheck.Refusing(IndexerConnectionOutcome.NotAnIndexer);
        }

        var caps = await ReadAsync(address!, apiKey, "t=caps", cancellationToken);
        if (caps.Refusal is { } capsRefusal)
        {
            return NewznabCheck.Refusing(capsRefusal, caps.Said);
        }

        var tree = CategoriesIn(caps.Document);
        logger.LogInformation(
            "The indexer at {Host} answered a search and offered {Count} top-level categories.",
            address!.Host,
            tree.Count);

        return new NewznabCheck(IndexerConnectionOutcome.Saved, Said: null, tree);
    }

    public async Task<NewznabCapsRead> CapsAsync(
        string url,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (!Address(url, out var address))
        {
            return new(IndexerConnectionOutcome.NotAnIndexer, null, []);
        }

        var read = await ReadAsync(address!, apiKey, "t=caps", cancellationToken);
        return new(read.Refusal, read.Said, read.Refusal is null ? CategoriesIn(read.Document) : []);
    }

    public async Task<NewznabSearchRead> SearchAsync(
        string url,
        string apiKey,
        IReadOnlyCollection<int> categoryIds,
        int offset,
        int? maxAgeDays,
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        if (!Address(url, out var address))
        {
            return NewznabSearchRead.Refusing(IndexerConnectionOutcome.NotAnIndexer);
        }

        var read = await ReadAsync(
            address!, apiKey, SearchQuery(categoryIds, offset, maxAgeDays, query, PageSize), cancellationToken);

        if (read.Refusal is { } refusal)
        {
            return NewznabSearchRead.Refusing(refusal, read.Said, read.RetryAfter);
        }

        var items = new List<NewznabRelease>();
        var dropped = 0;

        foreach (var item in read.Document?.Descendants().Where(element => element.Name.LocalName == "item") ?? [])
        {
            NewznabRelease? parsed;
            try
            {
                parsed = ReleaseIn(item);
            }
            catch (Exception malformed) when (malformed is FormatException or ArgumentException or OverflowException)
            {
                parsed = null;
            }
            if (parsed is null)
            {
                dropped++;
            }
            else
            {
                items.Add(parsed);
            }
        }

        return new(null, null, items, dropped, null);
    }

    /// <summary>
    /// Fetches the opaque NZB bytes at the credential-bearing address supplied
    /// by the indexer. The address never crosses this boundary or reaches a log.
    /// </summary>
    public async Task<NewznabNzbRead> NzbAsync(
        string downloadUrl,
        string indexerUrl,
        CancellationToken cancellationToken = default)
    {
        if (!NzbAddress(downloadUrl, indexerUrl, out var address))
        {
            return NewznabNzbRead.Refusing(IndexerConnectionOutcome.NotAnIndexer);
        }

        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(FabTransports.Indexers).GetAsync(
                address,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "The NZB from the indexer at {Host} could not be fetched: {Reason}.",
                address!.Host,
                unreachable.GetType().Name);
            return NewznabNzbRead.Refusing(IndexerConnectionOutcome.NotRightNow);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return NewznabNzbRead.Refusing(IndexerConnectionOutcome.LimitReached);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return NewznabNzbRead.Refusing(IndexerConnectionOutcome.WrongKey);
            }

            if (!response.IsSuccessStatusCode)
            {
                return NewznabNzbRead.Refusing(IndexerConnectionOutcome.NotRightNow);
            }

            try
            {
                var bytes = await BoundedAsync(response, IndexerAnswerCeiling.AnNzb, cancellationToken);

                if (bytes is null)
                {
                    logger.LogWarning(
                        "{Host} offered more than the ceiling of {Ceiling} bytes for one NZB.",
                        address!.Host,
                        IndexerAnswerCeiling.AnNzb);

                    return NewznabNzbRead.Refusing(IndexerConnectionOutcome.NotAnIndexer);
                }

                return bytes.Length == 0
                    ? NewznabNzbRead.Refusing(IndexerConnectionOutcome.NotAnIndexer)
                    : new(null, bytes);
            }
            catch (Exception unreadable) when (unreadable is HttpRequestException or IOException or TaskCanceledException
                                               && !cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "The NZB response from {Host} could not be read: {Reason}.",
                    address!.Host,
                    unreadable.GetType().Name);
                return NewznabNzbRead.Refusing(IndexerConnectionOutcome.NotRightNow);
            }
        }
    }

    internal static IReadOnlyList<CapsCategory> CategoriesIn(XDocument? document)
    {
        var categories = document?.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "categories")?
            .Elements().Where(element => element.Name.LocalName == "category");

        if (categories is null)
        {
            return [];
        }

        return
        [
            .. categories
                .Select(category => Node(category, SubcategoriesOf(category)))
                .OfType<CapsCategory>(),
        ];
    }

    private static IReadOnlyList<CapsCategory> SubcategoriesOf(XElement category) =>
    [
        .. category.Elements()
            .Where(element => element.Name.LocalName == "subcat")
            .Select(sub => Node(sub, []))
            .OfType<CapsCategory>(),
    ];

    private static CapsCategory? Node(XElement element, IReadOnlyList<CapsCategory> children)
    {
        var name = element.Attribute("name")?.Value;
        return !string.IsNullOrWhiteSpace(name)
            && int.TryParse(element.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? new CapsCategory(id, name, children)
                : null;
    }

    private async Task<NewznabDocument> ReadAsync(
        Uri address,
        string? apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        var request = With(address, $"{query}&apikey={Uri.EscapeDataString(apiKey ?? string.Empty)}");
        HttpResponseMessage response;

        try
        {
            response = await clients.CreateClient(FabTransports.Indexers).GetAsync(request, cancellationToken);
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("The indexer at {Host} did not answer: {Reason}.", address.Host, unreachable.GetType().Name);
            return NewznabDocument.Refusing(IndexerConnectionOutcome.NotRightNow);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var retryAfter = RetryAfter(response);
            string body;

            try
            {
                var bytes = await BoundedAsync(response, IndexerAnswerCeiling.ADocument, cancellationToken);

                if (bytes is null)
                {
                    logger.LogWarning(
                        "{Host} offered more than the ceiling of {Ceiling} bytes for one answer.",
                        address.Host,
                        IndexerAnswerCeiling.ADocument);

                    return NewznabDocument.Refusing(
                        IndexerConnectionOutcome.NotAnIndexer, retryAfter: retryAfter);
                }

                body = Text(response, bytes);
            }
            catch (Exception unreadable) when (unreadable is HttpRequestException or IOException or TaskCanceledException
                                               && !cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "The response from {Host} could not be read: {Reason}.",
                    address.Host,
                    unreadable.GetType().Name);
                return NewznabDocument.Refusing(IndexerConnectionOutcome.NotRightNow, retryAfter: retryAfter);
            }

            if (status == 429)
            {
                return NewznabDocument.Refusing(IndexerConnectionOutcome.LimitReached, retryAfter: retryAfter);
            }

            var document = Parse(Sanitise(body));

            if (document?.Root?.Name.LocalName == "error")
            {
                var described = SafeSaid(document.Root.Attribute("description")?.Value, apiKey);
                var code = int.TryParse(document.Root.Attribute("code")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : (int?)null;
                var outcome = IndexerConnection.ForError(status, code, described);
                logger.LogInformation(
                    "The indexer at {Host} refused: code {Code} at HTTP {Status}, read as {Outcome}.",
                    address.Host, code, status, outcome);
                return NewznabDocument.Refusing(outcome, described, retryAfter);
            }

            if (document is null)
            {
                return NewznabDocument.Refusing(
                    status is 401 or 403
                        ? IndexerConnection.ForError(status, errorCode: null, description: null)
                        : status is >= 500 and <= 599
                            ? IndexerConnectionOutcome.NotRightNow
                            : IndexerConnectionOutcome.NotAnIndexer,
                    retryAfter: retryAfter);
            }

            return response.IsSuccessStatusCode
                ? new(null, null, document, null)
                : NewznabDocument.Refusing(IndexerConnectionOutcome.NotRightNow, retryAfter: retryAfter);
        }
    }

    /// <summary>
    /// The body, up to <paramref name="ceiling"/>, or null where it went past it.
    /// </summary>
    /// <remarks>
    /// The same shape as <c>ArtworkGateway</c>'s bounded read and for the same
    /// reason: the indexer walk runs unattended against a service the user
    /// reached by pasting a URL, and buffering whatever it sends is how a
    /// misbehaving remote takes the container down rather than one run. Read
    /// rather than trusted, because a server that sends no <c>Content-Length</c>
    /// or lies about it is exactly the one this is for.
    /// </remarks>
    private static async Task<byte[]?> BoundedAsync(
        HttpResponseMessage response,
        long ceiling,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } announced && announced > ceiling)
        {
            return null;
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        using var kept = new MemoryStream();

        var buffer = new byte[64 * 1024];

        while (true)
        {
            var read = await body.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return kept.ToArray();
            }

            if (kept.Length + read > ceiling)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// The bytes as text, decoded the way <c>ReadAsStringAsync</c> would have:
    /// the charset the response declares, and UTF-8 where it declares none or
    /// names one this runtime does not have.
    /// </summary>
    private static string Text(HttpResponseMessage response, byte[] bytes)
    {
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"', ' ');

        if (string.IsNullOrEmpty(charset))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        try
        {
            return Encoding.GetEncoding(charset).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static NewznabRelease? ReleaseIn(XElement item)
    {
        string? Element(string name) => item.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        string? Attribute(string name) => item.Descendants()
            .Where(element => element.Name.LocalName.Equals("attr", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(element => element.Attribute("name")?.Value.Equals(name, StringComparison.OrdinalIgnoreCase) == true)?
            .Attribute("value")?.Value;

        var rawGuid = Element("guid")?.Trim();
        var identity = ReleaseIdentity.From(Attribute("guid"), rawGuid);
        if (identity is null) return null;

        var pubDate = Date(Element("pubDate"));
        var postDate = Date(Attribute("usenetdate")) ?? pubDate;
        if (pubDate is null || postDate is null) return null;

        var enclosure = item.Elements().FirstOrDefault(element => element.Name.LocalName == "enclosure");
        var sizeText = Attribute("size") ?? enclosure?.Attribute("length")?.Value;
        var categories = item.Descendants()
            .Where(element => element.Name.LocalName.Equals("attr", StringComparison.OrdinalIgnoreCase)
                && element.Attribute("name")?.Value.Equals("category", StringComparison.OrdinalIgnoreCase) == true)
            .Select(element => element.Attribute("value")?.Value)
            .Concat(item.Elements().Where(element => element.Name.LocalName == "category").Select(element => element.Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var title = Element("title")?.Trim() ?? string.Empty;
        var size = long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize)
            && parsedSize >= 0
                ? parsedSize
                : (long?)null;

        return new(
            identity,
            rawGuid ?? string.Empty,
            title,
            ComparisonForm.Of(title),
            size,
            categories,
            postDate.Value,
            pubDate.Value,
            Element("link")?.Trim() ?? enclosure?.Attribute("url")?.Value ?? string.Empty,
            Attribute("password")?.Trim());
    }

    private static DateTimeOffset? Date(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            return date.ToUniversalTime();
        }

        var withoutZone = ZoneAtEnd().Replace(value ?? string.Empty, " GMT");
        return DateTimeOffset.TryParse(withoutZone, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
            ? date.ToUniversalTime()
            : null;
    }

    private static XDocument? Parse(string body)
    {
        try
        {
            using var text = new StringReader(body);
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string Sanitise(string body) => IllegalXml().Replace(
        NamedHtmlEntities().Replace(body, match => WebUtility.HtmlEncode(WebUtility.HtmlDecode(match.Value))),
        string.Empty);

    private static string? SafeSaid(string? said, string? apiKey)
    {
        if (said is null) return null;
        var safe = AddressInText().Replace(said, "[address]");
        return string.IsNullOrEmpty(apiKey) ? safe : safe.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is not { } date) return null;
        var wait = date - time.GetUtcNow();
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private static bool Address(string? url, out Uri? address) =>
        Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out address)
        && address.Scheme is "http" or "https";

    /// <summary>
    /// Keeps an Indexer's credential on the transport the person configured.
    /// Some Newznab implementations advertise an HTTP enclosure even when
    /// their configured API endpoint is HTTPS, then redirect that enclosure
    /// to the same host over HTTPS. The credentialled transport still follows
    /// no redirect: this upgrades the first request itself, and refuses an HTTP
    /// enclosure on another host because it cannot be upgraded by evidence the
    /// person supplied.
    /// </summary>
    private static bool NzbAddress(string downloadUrl, string indexerUrl, out Uri? address)
    {
        if (!Address(downloadUrl, out address) || !Address(indexerUrl, out var indexer)) return false;
        if (address!.Scheme != Uri.UriSchemeHttp || indexer!.Scheme != Uri.UriSchemeHttps) return true;
        if (!string.Equals(address.IdnHost, indexer.IdnHost, StringComparison.OrdinalIgnoreCase)) return false;

        var upgraded = new UriBuilder(address)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = indexer.IsDefaultPort ? -1 : indexer.Port,
        };
        address = upgraded.Uri;
        return true;
    }

    private static string SearchQuery(IReadOnlyCollection<int> categoryIds, int offset, int? maxAgeDays, string? query, int limit)
    {
        var parts = new List<string> { "t=search", "extended=1", $"limit={limit}", $"offset={offset}" };
        if (categoryIds.Count > 0) parts.Add($"cat={string.Join(',', categoryIds)}");
        if (maxAgeDays is not null) parts.Add($"maxage={maxAgeDays.Value}");
        if (!string.IsNullOrWhiteSpace(query)) parts.Add($"q={Uri.EscapeDataString(query)}");
        return string.Join('&', parts);
    }

    private static Uri With(Uri address, string query)
    {
        var builder = new UriBuilder(address);
        var existing = builder.Query.TrimStart('?');
        builder.Query = existing.Length > 0 ? $"{existing}&{query}" : query;
        return builder.Uri;
    }

    [GeneratedRegex(@"&(?!amp;|lt;|gt;|quot;|apos;)([A-Za-z][A-Za-z0-9]+);")]
    private static partial Regex NamedHtmlEntities();

    [GeneratedRegex("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]")]
    private static partial Regex IllegalXml();

    [GeneratedRegex(@"\s+[A-Z]{2,5}$")]
    private static partial Regex ZoneAtEnd();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AddressInText();

    private sealed record NewznabDocument(
        IndexerConnectionOutcome? Refusal,
        string? Said,
        XDocument? Document,
        TimeSpan? RetryAfter)
    {
        public static NewznabDocument Refusing(
            IndexerConnectionOutcome outcome,
            string? said = null,
            TimeSpan? retryAfter = null) => new(outcome, said, null, retryAfter);
    }
}

public sealed record NewznabCapsRead(
    IndexerConnectionOutcome? Refusal,
    string? Said,
    IReadOnlyList<CapsCategory> Categories);

public sealed record NewznabCheck(
    IndexerConnectionOutcome Outcome,
    string? Said,
    IReadOnlyList<CapsCategory> Categories)
{
    public static NewznabCheck Refusing(IndexerConnectionOutcome outcome, string? said = null) =>
        new(outcome, said, []);
}

public sealed record NewznabSearchRead(
    IndexerConnectionOutcome? Refusal,
    string? Said,
    IReadOnlyList<NewznabRelease> Releases,
    int DroppedWithoutIdentity,
    TimeSpan? RetryAfter)
{
    public static NewznabSearchRead Refusing(
        IndexerConnectionOutcome outcome,
        string? said = null,
        TimeSpan? retryAfter = null) => new(outcome, said, [], 0, retryAfter);
}

public sealed record NewznabRelease(
    string DerivedReleaseId,
    string RawGuid,
    string Title,
    string NormalisedTitle,
    long? Size,
    IReadOnlyList<string> Categories,
    DateTimeOffset PostDate,
    DateTimeOffset PubDate,
    string DownloadUrl,
    string? Password = null);

public sealed record NewznabNzbRead(
    IndexerConnectionOutcome? Refusal,
    byte[] Bytes)
{
    public static NewznabNzbRead Refusing(IndexerConnectionOutcome outcome) => new(outcome, []);
}
