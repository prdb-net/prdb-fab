using System.Net;

using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The one place an image is fetched from a CDN. ADR 0030's fetch, and the
/// three limits that stand in the governor's place.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is not the prdb transport and must not become one.</strong> An
/// image URL is an absolute URL prdb hands out in its own payload, and asking
/// for it is a <c>GET</c> against a content delivery network: no API key, no
/// entry in the rate-limit headers ADR 0013 reads the budget from, and
/// therefore nothing for ADR 0014's governor to spend. Putting it under the
/// governor would make a library grid compete with identification for prdb
/// requests it never made.
/// </para>
/// <para>
/// <strong>The limits are constants because there is no budget to read.</strong>
/// The timeout and the redirect rule are the transport's
/// (<see cref="FabTransports.Artwork"/>); the size ceiling and the content
/// check are here, because they are about the answer rather than about the
/// call.
/// </para>
/// </remarks>
public sealed class ArtworkGateway(IHttpClientFactory clients, ILogger<ArtworkGateway> logger)
{
    /// <summary>
    /// Fetches one image, or says which of the two ways it did not arrive.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point of the return type. ADR 0030 marks a
    /// dead URL once and never retries it, and ADR 0016 already drew the line
    /// this rests on: a request that failed is not an id that was genuinely
    /// absent. Collapsing them would turn one flaky minute into a grid of
    /// permanent blanks.
    /// </remarks>
    public async Task<ArtworkFetch> FetchAsync(string? url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            // Not a transport failure: prdb published something that is not an
            // address, and asking again will produce the same string. That is
            // the same fact as a 404 and is marked like one.
            return ArtworkFetch.Dead;
        }

        var client = clients.CreateClient(FabTransports.Artwork);

        try
        {
            using var answer = await client.GetAsync(
                address,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (answer.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // prdb hard-deletes image rows, so this is normally permanent.
                return ArtworkFetch.Dead;
            }

            if (!answer.IsSuccessStatusCode)
            {
                // A 5xx, a 403 from a CDN having a bad day, a redirect that went
                // nowhere. None of those says the image is gone.
                return ArtworkFetch.Refused(answer.StatusCode);
            }

            if (answer.Content.Headers.ContentLength is { } announced
                && announced > ArtworkCeiling.AnImage)
            {
                logger.LogWarning(
                    "{Host} offered an image of {Bytes} bytes, over the ceiling of {Ceiling}.",
                    address.Host,
                    announced,
                    ArtworkCeiling.AnImage);

                return ArtworkFetch.TooLarge;
            }

            var bytes = await ReadAsync(answer, cancellationToken);

            if (bytes is null)
            {
                logger.LogWarning(
                    "{Host} sent more than the ceiling of {Ceiling} bytes for one image.",
                    address.Host,
                    ArtworkCeiling.AnImage);

                return ArtworkFetch.TooLarge;
            }

            if (!ArtworkFormat.IsAnImage(bytes))
            {
                // ADR 0030's content check. What it catches is an answer that
                // is not artwork at all — a captive portal, an error page served
                // with a 200 — rather than an image in a format nothing here
                // knows about, which is why it is by signature and refuses
                // little.
                logger.LogWarning(
                    "{Host} answered with {Bytes} bytes that are not an image.",
                    address.Host,
                    bytes.Length);

                return ArtworkFetch.NotAnImage;
            }

            return ArtworkFetch.Arrived(bytes);
        }
        catch (HttpRequestException failed)
        {
            logger.LogDebug(failed, "Fetching an image from {Host} failed.", address.Host);

            return ArtworkFetch.Unreachable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The transport's timeout, which on the display path is short by
            // design: the no-artwork tile is the answer, and the next request
            // for the same image tries again.
            logger.LogDebug("Fetching an image from {Host} timed out.", address.Host);

            return ArtworkFetch.Unreachable;
        }
    }

    /// <summary>
    /// The body, up to the ceiling, or null where it went past it.
    /// </summary>
    /// <remarks>
    /// Read rather than trusted: <c>Content-Length</c> is checked above where it
    /// is offered, and a server that sends no length or lies about it is
    /// stopped here instead.
    /// </remarks>
    private static async Task<byte[]?> ReadAsync(
        HttpResponseMessage answer,
        CancellationToken cancellationToken)
    {
        await using var body = await answer.Content.ReadAsStreamAsync(cancellationToken);

        using var kept = new MemoryStream();

        var buffer = new byte[64 * 1024];

        while (true)
        {
            var read = await body.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return kept.ToArray();
            }

            if (kept.Length + read > ArtworkCeiling.AnImage)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }
    }

}

/// <summary>
/// What one fetch produced: the bytes, or the reason there are none.
/// </summary>
/// <param name="Bytes">The image, where one arrived.</param>
/// <param name="UrlIsDead">
/// Whether what happened says the URL will not work again. ADR 0030 marks the
/// row once on this and never asks a second time; everything else leaves no
/// mark at all.
/// </param>
/// <param name="Reason">
/// A sentence for whoever reads the run log, and never read for control flow
/// (ADR 0016, ADR 0043).
/// </param>
public sealed record ArtworkFetch(byte[]? Bytes, bool UrlIsDead, string? Reason)
{
    public static ArtworkFetch Arrived(byte[] bytes) => new(bytes, UrlIsDead: false, Reason: null);

    public static ArtworkFetch Dead { get; } =
        new(Bytes: null, UrlIsDead: true, "The image is no longer published.");

    public static ArtworkFetch Unreachable { get; } =
        new(Bytes: null, UrlIsDead: false, "The image could not be reached.");

    public static ArtworkFetch TooLarge { get; } =
        new(Bytes: null, UrlIsDead: false, "The image is larger than the cache accepts.");

    public static ArtworkFetch NotAnImage { get; } =
        new(Bytes: null, UrlIsDead: false, "What was served is not an image.");

    public static ArtworkFetch Refused(HttpStatusCode status) =>
        new(Bytes: null, UrlIsDead: false, $"The image was refused with {(int)status}.");
}
