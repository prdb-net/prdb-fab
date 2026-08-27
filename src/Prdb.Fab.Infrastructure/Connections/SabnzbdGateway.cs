using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The one place SABnzbd is reached from, and in this slice it reads and never
/// writes.
/// </summary>
/// <remarks>
/// ADR 0016 allows exactly one write to SABnzbd — <c>addfile</c> — and every
/// call here is a read, so that rule is still intact when this slice ends.
/// </remarks>
public sealed class SabnzbdGateway(IHttpClientFactory clients, ILogger<SabnzbdGateway> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// SABnzbd's own categories, each with the folder its finished downloads
    /// land under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three reads, and the first of them is the key check ADR 0010 asks for.
    /// <c>get_cats</c> needs the full API key, which is what makes it a check
    /// worth the name: <c>version</c> and <c>auth</c> answer without one and
    /// would happily confirm a wrong key, and the NZB key — which can submit
    /// downloads — cannot reach this.
    /// </para>
    /// <para>
    /// The other two are what turns a category into a path. <c>fullstatus</c>
    /// gives the completed-downloads folder as SABnzbd resolved it, and
    /// <c>get_config</c> gives each category's own folder, which overrides that
    /// one when it is absolute. Neither is guessable, and getting it wrong is
    /// not visible until the first finished download.
    /// </para>
    /// </remarks>
    public async Task<SabnzbdCategories> CategoriesAsync(
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (BaseAddressOf(url) is not { } address)
        {
            return new SabnzbdCategories(SabnzbdConnectionOutcome.NotSabnzbd, []);
        }

        var client = clients.CreateClient(FabTransports.Sabnzbd);

        var (catsRefusal, cats) = await ReadAsync<CatsBody>(client, address, apiKey, "get_cats", cancellationToken);
        if (catsRefusal is { } refusedCats)
        {
            return new SabnzbdCategories(refusedCats, []);
        }

        var (statusRefusal, status) = await ReadAsync<StatusEnvelope>(
            client, address, apiKey, "fullstatus&skip_dashboard=1", cancellationToken);
        if (statusRefusal is { } refusedStatus)
        {
            return new SabnzbdCategories(refusedStatus, []);
        }

        var (configRefusal, config) = await ReadAsync<ConfigEnvelope>(
            client, address, apiKey, "get_config&section=categories", cancellationToken);
        if (configRefusal is { } refusedConfig)
        {
            return new SabnzbdCategories(refusedConfig, []);
        }

        if (cats?.Categories is not { } names || status?.Status?.CompleteDir is not { Length: > 0 } completed)
        {
            return new SabnzbdCategories(SabnzbdConnectionOutcome.NotSabnzbd, []);
        }

        var folders = config?.Config?.Categories ?? [];

        var categories = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new SabnzbdCategory(
                name,
                SabnzbdConnection.CompletedRoot(
                    completed,
                    folders.FirstOrDefault(folder =>
                        string.Equals(folder.Name, name, StringComparison.Ordinal))?.Dir)))
            .ToArray();

        logger.LogInformation(
            "SABnzbd at {Host} answered with {Count} categories.",
            address.Host,
            categories.Length);

        return new SabnzbdCategories(SabnzbdConnectionOutcome.Saved, categories);
    }

    private async Task<(SabnzbdConnectionOutcome? Refusal, T? Body)> ReadAsync<T>(
        HttpClient client,
        Uri address,
        string? apiKey,
        string mode,
        CancellationToken cancellationToken)
    {
        // ADR 0037 and the SABnzbd research both land here: the key travels in
        // the query string because SABnzbd accepts it nowhere else. There is no
        // header form. That is also why ADR 0041 forbids this transport from
        // following a redirect.
        var request = new Uri(
            address,
            $"api?mode={mode}&output=json&apikey={Uri.EscapeDataString(apiKey ?? string.Empty)}");

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(request, cancellationToken);
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException
                                            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "SABnzbd at {Host} did not answer: {Reason}.",
                address.Host,
                unreachable.GetType().Name);

            return (SabnzbdConnectionOutcome.NotRightNow, default);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // SABnzbd answers 403 with a bare plain-text line, and with an
                // empty body when its API warnings are turned off. Two different
                // problems arrive that way — a key it does not accept, and a
                // request it will not take from this address at all — and they
                // have different fixes, so the line is worth reading.
                var said = await response.Content.ReadAsStringAsync(cancellationToken);

                var refusal = said.Contains("Key", StringComparison.OrdinalIgnoreCase)
                    ? SabnzbdConnectionOutcome.WrongKey
                    : SabnzbdConnectionOutcome.AccessDenied;

                logger.LogInformation("SABnzbd at {Host} refused: {Outcome}.", address.Host, refusal);

                return (refusal, default);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (SabnzbdConnectionOutcome.NotSabnzbd, default);
            }

            try
            {
                var body = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);

                return body is null ? (SabnzbdConnectionOutcome.NotSabnzbd, default) : (null, body);
            }
            catch (JsonException)
            {
                // An HTML login page, a reverse proxy's error, or something else
                // entirely. Whatever it was, it was not SABnzbd's API.
                return (SabnzbdConnectionOutcome.NotSabnzbd, default);
            }
        }
    }

    /// <summary>
    /// The address the API hangs off, with the trailing slash that makes it one.
    /// </summary>
    /// <remarks>
    /// Without it, <c>http://host:8080/sabnzbd</c> resolves <c>api</c> against
    /// the host rather than against the prefix — a URL that answers 404 while
    /// looking exactly like the one that works. People do run it behind a path.
    /// </remarks>
    private static Uri? BaseAddressOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return address.AbsolutePath.EndsWith('/')
            ? address
            : new UriBuilder(address) { Path = address.AbsolutePath + "/" }.Uri;
    }

    private sealed record CatsBody(
        [property: JsonPropertyName("categories")] string[]? Categories);

    private sealed record StatusEnvelope(
        [property: JsonPropertyName("status")] StatusBody? Status);

    private sealed record StatusBody(
        [property: JsonPropertyName("completedir")] string? CompleteDir);

    private sealed record ConfigEnvelope(
        [property: JsonPropertyName("config")] ConfigBody? Config);

    private sealed record ConfigBody(
        [property: JsonPropertyName("categories")] CategoryFolder[]? Categories);

    private sealed record CategoryFolder(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("dir")] string? Dir);
}

/// <summary>One of SABnzbd's own categories, and where its downloads finish.</summary>
/// <param name="CompletedRoot">
/// The folder as SABnzbd sees it, which need not exist in this container — that
/// is what the path mapping is for.
/// </param>
public sealed record SabnzbdCategory(string Name, string CompletedRoot);

/// <summary>
/// What SABnzbd answered when it was asked for its categories.
/// <see cref="SabnzbdConnectionOutcome.Saved"/> here means it answered; whether
/// anything is stored is the caller's.
/// </summary>
public sealed record SabnzbdCategories(
    SabnzbdConnectionOutcome Outcome,
    IReadOnlyList<SabnzbdCategory> Categories);
