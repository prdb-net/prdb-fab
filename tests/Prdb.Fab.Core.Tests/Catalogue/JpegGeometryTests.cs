using Prdb.Fab.Core.Catalogue;

using Xunit;

namespace Prdb.Fab.Core.Tests.Catalogue;

/// <summary>
/// Reading a sprite sheet's real size out of its own frame header, which is
/// what ADR 0061 validates a WebVTT against rather than the payload that
/// described the file.
/// </summary>
public sealed class JpegGeometryTests
{
    /// <summary>
    /// A baseline JPEG with the segments a real encoder writes in front of the
    /// frame: the geometry is behind them and is found by walking rather than
    /// by looking at a fixed offset.
    /// </summary>
    [Fact]
    public void The_frame_header_is_found_behind_the_segments_in_front_of_it()
    {
        var jpeg = Jpeg(0xC0, 3200, 1800, withSegmentsInFront: true);

        Assert.Equal((3200, 1800), JpegGeometry.Of(jpeg));
    }

    /// <summary>
    /// Progressive JPEGs are ordinary for a large sheet, and their frame marker
    /// is a different one. Every start-of-frame in the range counts.
    /// </summary>
    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC1)]
    [InlineData(0xC2)]
    [InlineData(0xC9)]
    public void Every_start_of_frame_marker_answers(byte marker)
    {
        Assert.Equal((640, 360), JpegGeometry.Of(Jpeg(marker, 640, 360, withSegmentsInFront: false)));
    }

    /// <summary>
    /// The four markers in the same numeric range that are not frames. Reading
    /// a Huffman table as a frame header would produce a plausible-looking size
    /// out of somebody else's bytes.
    /// </summary>
    [Theory]
    [InlineData(0xC4)]
    [InlineData(0xC8)]
    [InlineData(0xCC)]
    public void A_marker_in_the_range_that_is_not_a_frame_is_not_read_as_one(byte marker)
    {
        // The only frame-shaped thing in this file is the impostor, so an
        // answer at all would be the wrong one.
        Assert.Null(JpegGeometry.Of(Jpeg(marker, 1234, 5678, withSegmentsInFront: false)));
    }

    [Fact]
    public void Bytes_that_are_not_a_jpeg_have_no_geometry()
    {
        Assert.Null(JpegGeometry.Of([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Null(JpegGeometry.Of([]));
        Assert.Null(JpegGeometry.Of("WEBVTT"u8));
    }

    /// <summary>
    /// A file truncated before its frame header answers nothing rather than
    /// reading past its own end.
    /// </summary>
    [Fact]
    public void A_truncated_jpeg_has_no_geometry()
    {
        var jpeg = Jpeg(0xC0, 3200, 1800, withSegmentsInFront: true);

        Assert.Null(JpegGeometry.Of(jpeg.AsSpan(0, 12)));
    }

    /// <summary>
    /// A file whose scan begins before any frame header — malformed, or
    /// deliberately shaped to be read past — answers nothing.
    /// </summary>
    [Fact]
    public void A_scan_before_the_frame_stops_the_walk()
    {
        byte[] jpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 0x3F, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x00, 0x01, 0x00,
        ];

        Assert.Null(JpegGeometry.Of(jpeg));
    }

    /// <summary>
    /// A JPEG as far as anything in this project reads one: the markers, the
    /// lengths and the frame header, with no entropy-coded data behind them
    /// because nothing here decodes a picture.
    /// </summary>
    private static byte[] Jpeg(byte marker, int width, int height, bool withSegmentsInFront)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        if (withSegmentsInFront)
        {
            // APP0/JFIF, then a quantisation table: what every encoder puts in
            // front of the frame.
            bytes.AddRange([0xFF, 0xE0, 0x00, 0x10]);
            bytes.AddRange("JFIF\0"u8.ToArray());
            bytes.AddRange([0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
            bytes.AddRange([0xFF, 0xDB, 0x00, 0x05, 0x00, 0x01, 0x02]);

            // A fill byte before the next marker, which is legal and is the
            // thing a naive walk trips over.
            bytes.Add(0xFF);
        }

        bytes.AddRange(
        [
            0xFF,
            marker,
            0x00,
            0x11,
            0x08,
            (byte)(height >> 8),
            (byte)(height & 0xFF),
            (byte)(width >> 8),
            (byte)(width & 0xFF),
            0x03,
            0x01, 0x22, 0x00,
            0x02, 0x11, 0x01,
            0x03, 0x11, 0x01,
        ]);

        bytes.AddRange([0xFF, 0xD9]);

        return [.. bytes];
    }
}
