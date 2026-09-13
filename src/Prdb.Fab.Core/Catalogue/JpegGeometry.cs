namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// How large a JPEG really is, read out of the file's own frame header.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0061 validates a sprite sheet's WebVTT against the sheet, and the sheet
/// has to be the file rather than the payload that described it. prdb reports a
/// <c>width</c> and a <c>height</c> beside every user preview, but those come
/// from whoever uploaded it: a cue naming a rectangle inside the <em>declared</em>
/// dimensions and outside the real ones would be a tile drawn from nothing, and
/// checking against the submitter's own number is checking a claim against
/// itself.
/// </para>
/// <para>
/// <strong>Reading a header is not decoding an image.</strong> This walks the
/// marker segments to the start-of-frame and reads four bytes; it never touches
/// a scan, allocates nothing, and cannot be made to do work proportional to the
/// picture. That is the whole reason this project can ask the question at all
/// without taking an imaging dependency, which ADR 0004's stack does not have
/// and ADR 0036's argument would not carry here.
/// </para>
/// <para>
/// JPEG only, because that is what prdb serves: the upload endpoint takes a
/// JPEG, and <see cref="ArtworkFormat"/> is the wider question asked of the
/// other cache.
/// </para>
/// </remarks>
public static class JpegGeometry
{
    /// <summary>
    /// The width and height of this JPEG, or null where the bytes are not one
    /// or do not carry a frame header.
    /// </summary>
    public static (int Width, int Height)? Of(ReadOnlySpan<byte> bytes)
    {
        // SOI.
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        var at = 2;

        while (at + 3 < bytes.Length)
        {
            if (bytes[at] != 0xFF)
            {
                // Not at a marker. A JPEG is a sequence of them up to the scan,
                // so this is a file that is malformed or truncated rather than
                // one whose header is further on.
                return null;
            }

            var marker = bytes[at + 1];

            // Fill bytes: any number of 0xFF may precede a marker.
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            // Standalone markers, which carry no length: RSTn, SOI, EOI, TEM.
            if (marker is (>= 0xD0 and <= 0xD9) or 0x01)
            {
                at += 2;
                continue;
            }

            if (at + 3 >= bytes.Length)
            {
                return null;
            }

            var length = (bytes[at + 2] << 8) | bytes[at + 3];

            if (length < 2)
            {
                return null;
            }

            // Every start-of-frame there is — baseline, progressive, lossless,
            // arithmetic — minus the four in the range that are not frames:
            // DHT (C4), JPG (C8), DAC (CC) and the RSTn above.
            if (marker is (>= 0xC0 and <= 0xCF) and not (0xC4 or 0xC8 or 0xCC))
            {
                if (at + 9 >= bytes.Length)
                {
                    return null;
                }

                var height = (bytes[at + 5] << 8) | bytes[at + 6];
                var width = (bytes[at + 7] << 8) | bytes[at + 8];

                return width > 0 && height > 0 ? (width, height) : null;
            }

            // Start of scan: the frame header is behind us if it was there at
            // all, and what follows is entropy-coded data rather than markers.
            if (marker == 0xDA)
            {
                return null;
            }

            at += 2 + length;
        }

        return null;
    }
}
