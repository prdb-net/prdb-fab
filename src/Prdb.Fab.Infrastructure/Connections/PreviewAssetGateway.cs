using System.Net;

using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// The one place a user preview's bytes are fetched from. ADR 0061's fetch, and
/// the limits that stand in the governor's place.
/// </summary>
/// <remarks>
/// <para>
/// Beside <see cref="ArtworkGateway"/> rather than inside it, on the transport
/// it already owns. The argument for not putting either under the prdb governor
/// is unchanged and is ADR 0030's: these are <c>GET</c>s against a content
/// delivery network, with no API key and no entry in the rate-limit headers, so
/// there is nothing for ADR 0014 to spend. What differs is what may come back —
/// a sprite sheet is an order of magnitude larger than a poster, and half of
/// what is fetched here is not an image at all — so the ceilings and the content
/// checks are this class's own.
/// </para>
/// <para>
/// <strong>A dead URL means nothing permanent here.</strong> ADR 0030 marks an
/// <c>images[]</c> entry dead once and never asks again, because prdb
/// hard-deletes those rows. A user preview's object may move or expire while the
/// row goes on being published, so a <c>404</c> costs this attempt and nothing
/// else — there is no <c>FoundDead</c> for this population, and ADR 0061 says so
/// in as many words.
/// </para>
/// </remarks>
public sealed class PreviewAssetGateway(IHttpClientFactory clients, ILogger<PreviewAssetGateway> logger)
{
    /// <summary>Fetches one sprite sheet or single image.</summary>
    public Task<PreviewFetch> ImageAsync(string? url, CancellationToken cancellationToken) =>
        FetchAsync(
            url,
            UserPreviewContract.ASprite,
            "image",
            bytes => ArtworkFormat.MediaTypeOf(bytes) == "image/jpeg",
            cancellationToken);

    /// <summary>Fetches the WebVTT paired with a sprite sheet.</summary>
    /// <remarks>
    /// Checked for its signature the same way an image is checked for its own,
    /// and for the same reason: what the check catches is an answer that is not
    /// the thing at all — a captive portal, an error page served with a 200 —
    /// rather than a file this parser will later refuse for a better reason.
    /// </remarks>
    public Task<PreviewFetch> VttAsync(string? url, CancellationToken cancellationToken) =>
        FetchAsync(url, UserPreviewContract.AVtt, "WebVTT", IsWebVtt, cancellationToken);

    private async Task<PreviewFetch> FetchAsync(
        string? url,
        long ceiling,
        string what,
        Func<byte[], bool> accepts,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            return PreviewFetch.Refused($"prdb published something that is not an address for a {what}.");
        }

        var client = clients.CreateClient(FabTransports.Artwork);

        try
        {
            using var answer = await client.GetAsync(
                address,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!answer.IsSuccessStatusCode)
            {
                // Including 404 and 410. See the class remarks: an object that
                // has gone while its row is still published is this attempt's
                // problem and nothing more.
                logger.LogDebug(
                    "{Host} answered {Status} for a user preview {What}.",
                    address.Host,
                    (int)answer.StatusCode,
                    what);

                return PreviewFetch.Refused($"The {what} was answered with {(int)answer.StatusCode}.");
            }

            if (answer.Content.Headers.ContentLength is { } announced && announced > ceiling)
            {
                return PreviewFetch.Refused($"The {what} is larger than the cache accepts.");
            }

            var bytes = await BoundedBody.ReadAsync(answer, ceiling, cancellationToken);

            if (bytes is null)
            {
                logger.LogWarning(
                    "{Host} sent more than the ceiling of {Ceiling} bytes for a user preview {What}.",
                    address.Host,
                    ceiling,
                    what);

                return PreviewFetch.Refused($"The {what} is larger than the cache accepts.");
            }

            if (!accepts(bytes))
            {
                logger.LogWarning(
                    "{Host} answered with {Bytes} bytes that are not a user preview {What}.",
                    address.Host,
                    bytes.Length,
                    what);

                return PreviewFetch.Refused($"What was served is not a {what}.");
            }

            return PreviewFetch.Arrived(bytes);
        }
        catch (HttpRequestException failed)
        {
            logger.LogDebug(failed, "Fetching a user preview from {Host} failed.", address.Host);

            return PreviewFetch.Refused("The asset could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Fetching a user preview from {Host} timed out.", address.Host);

            return PreviewFetch.Refused("The asset could not be reached in time.");
        }
    }

    private static bool IsWebVtt(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = "WEBVTT"u8;
        var body = bytes.AsSpan();

        if (body.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF)
        {
            body = body[3..];
        }

        return body.Length >= signature.Length && body[..signature.Length].SequenceEqual(signature);
    }
}

/// <summary>
/// What one fetch of a user preview's asset produced.
/// </summary>
/// <remarks>
/// One case fewer than <see cref="ArtworkFetch"/>, and the missing one is the
/// point: there is no <em>dead</em> here, because nothing about this population
/// is permanent. A preview stops being served when prdb stops showing it, which
/// arrives on the change feed rather than as a status code.
/// </remarks>
/// <param name="Bytes">What arrived, or null.</param>
/// <param name="Reason">
/// A sentence for whoever reads the run log, never read for control flow
/// (ADR 0016, ADR 0043).
/// </param>
public sealed record PreviewFetch(byte[]? Bytes, string? Reason)
{
    public static PreviewFetch Arrived(byte[] bytes) => new(bytes, null);

    public static PreviewFetch Refused(string reason) => new(null, reason);
}

/// <summary>
/// A response body up to a ceiling, or nothing where it went past it.
/// </summary>
/// <remarks>
/// Read rather than trusted: <c>Content-Length</c> is checked by both callers
/// where it is offered, and a server that sends no length or lies about it is
/// stopped here instead. Shared by the two gateways because it is the same
/// sentence twice otherwise, and the ceiling is the only thing that differs.
/// </remarks>
internal static class BoundedBody
{
    public static async Task<byte[]?> ReadAsync(
        HttpResponseMessage answer,
        long ceiling,
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

            if (kept.Length + read > ceiling)
            {
                return null;
            }

            kept.Write(buffer, 0, read);
        }
    }
}
