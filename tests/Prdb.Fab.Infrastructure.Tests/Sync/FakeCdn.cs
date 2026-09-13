using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// A content delivery network, pretending, counting what it was asked for.
/// </summary>
/// <remarks>
/// ADR 0042 replaces the network at the socket, so what sits above this is the
/// real artwork transport with its real timeout and its real redirect rule —
/// which is the half of ADR 0030 that a stubbed gateway would not exercise at
/// all.
/// </remarks>
internal sealed class FakeCdn : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> images = [];
    private readonly Dictionary<string, string> types = [];
    private readonly HashSet<string> gone = [];

    /// <summary>Every URL asked for, in the order it was asked for.</summary>
    public List<string> Asked { get; } = [];

    public int Requests => Asked.Count;

    /// <summary>Serves <paramref name="bytes"/> bytes of PNG at this URL.</summary>
    public FakeCdn Serves(string url, int bytes = 1024)
    {
        images[url] = Png(bytes);

        return this;
    }

    /// <summary>
    /// Serves a sprite sheet of this size at this URL (ADR 0061).
    /// </summary>
    /// <remarks>
    /// A JPEG as far as anything in this project reads one: the signature, the
    /// frame header the geometry is read out of, and padding to the weight the
    /// test wants the cache to see. Nothing here decodes a picture.
    /// </remarks>
    public FakeCdn ServesSprite(string url, int width, int height, int bytes = 4096)
    {
        images[url] = Jpeg(width, height, bytes);
        types[url] = "image/jpeg";

        return this;
    }

    /// <summary>Serves this text at this URL, as whatever it is.</summary>
    public FakeCdn ServesText(string url, string body, string contentType = "text/vtt")
    {
        images[url] = Encoding.UTF8.GetBytes(body);
        types[url] = contentType;

        return this;
    }

    /// <summary>Serves exactly these bytes at this URL.</summary>
    public FakeCdn ServesBytes(string url, byte[] bytes, string contentType = "application/octet-stream")
    {
        images[url] = bytes;
        types[url] = contentType;

        return this;
    }

    /// <summary>Answers 404 at this URL, which is ADR 0030's dead URL.</summary>
    public FakeCdn Lost(string url)
    {
        gone.Add(url);

        return this;
    }

    /// <summary>How many bytes this URL serves, which is what eviction weighs.</summary>
    public int Weight(string url) => images[url].Length;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();

        Asked.Add(url);

        if (gone.Contains(url))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (!images.TryGetValue(url, out var bytes))
        {
            // Nothing was arranged for this address, which is a fetch the test
            // did not expect rather than an image that is gone. A 500 leaves no
            // mark on the row, so a test that trips over it fails on what it is
            // actually about.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }

        var answer = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };

        answer.Content.Headers.ContentType = new MediaTypeHeaderValue(
            types.TryGetValue(url, out var type) ? type : "image/png");

        return Task.FromResult(answer);
    }

    /// <summary>
    /// A PNG signature and then padding, which is exactly as much of an image as
    /// ADR 0030's content check reads.
    /// </summary>
    /// <summary>
    /// A baseline JPEG's signature and frame header, padded to
    /// <paramref name="bytes"/>. The padding sits after the end-of-image
    /// marker, where a decoder would ignore it and where the weight is all the
    /// cache is reading it for.
    /// </summary>
    private static byte[] Jpeg(int width, int height, int bytes)
    {
        byte[] header =
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)(height & 0xFF),
            (byte)(width >> 8), (byte)(width & 0xFF),
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xFF, 0xD9,
        ];

        var image = new byte[Math.Max(bytes, header.Length)];

        header.CopyTo(image, 0);

        return image;
    }

    private static byte[] Png(int bytes)
    {
        var image = new byte[Math.Max(bytes, 8)];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        signature.CopyTo(image);

        return image;
    }
}
