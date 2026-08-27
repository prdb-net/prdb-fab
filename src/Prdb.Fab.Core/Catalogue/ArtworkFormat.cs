namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// What an image is, read off its first bytes rather than off a header somebody
/// sent with it.
/// </summary>
/// <remarks>
/// <para>
/// Two callers, one answer, which is why this is a rule rather than a detail of
/// either. ADR 0030 asks for a content check before the bytes are kept — that
/// is the fetch — and the display path has to say what it is serving, which is
/// the same question asked of the same bytes.
/// </para>
/// <para>
/// <strong>By signature and not by <c>Content-Type</c>.</strong> The header is
/// what a misconfigured CDN gets wrong and the first bytes are what a browser
/// will actually decode. It is also what keeps the media type off the row: a
/// stored type would be a column with a writer, no reader that would notice it
/// drifting, and a file on disk that already carries the answer.
/// </para>
/// <para>
/// The formats artwork is served in, and nothing else. What the refusal catches
/// is an answer that is not an image at all — a captive portal, an error page
/// served with a 200 — so it is drawn wide on purpose.
/// </para>
/// </remarks>
public static class ArtworkFormat
{
    /// <summary>
    /// How many bytes have to be in hand for <see cref="MediaTypeOf"/> to
    /// answer.
    /// </summary>
    public const int Header = 16;

    /// <summary>
    /// The media type these bytes begin with, or null where they are not an
    /// image this cache keeps.
    /// </summary>
    public static string? MediaTypeOf(ReadOnlySpan<byte> bytes)
    {
        if (At(bytes, 0, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (At(bytes, 0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (At(bytes, 0, "GIF87a") || At(bytes, 0, "GIF89a"))
        {
            return "image/gif";
        }

        // RIFF....WEBP: the four bytes between the two are the length, which
        // says nothing about the format.
        if (At(bytes, 0, "RIFF") && At(bytes, 8, "WEBP"))
        {
            return "image/webp";
        }

        // An ISO base media file, which is what AVIF is one brand of. The brand
        // itself is read rather than assumed, because the same container holds
        // video and this cache does not keep any.
        if (At(bytes, 4, "ftyp"))
        {
            return At(bytes, 8, "avif") || At(bytes, 8, "avis") ? "image/avif" : null;
        }

        return null;
    }

    /// <summary>Whether these bytes begin the way an image does.</summary>
    public static bool IsAnImage(ReadOnlySpan<byte> bytes) => MediaTypeOf(bytes) is not null;

    private static bool At(ReadOnlySpan<byte> bytes, int offset, string signature)
    {
        if (bytes.Length < offset + signature.Length)
        {
            return false;
        }

        for (var index = 0; index < signature.Length; index++)
        {
            if (bytes[offset + index] != (byte)signature[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool At(ReadOnlySpan<byte> bytes, int offset, ReadOnlySpan<byte> signature) =>
        bytes.Length >= offset + signature.Length
        && bytes.Slice(offset, signature.Length).SequenceEqual(signature);
}
