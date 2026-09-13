using System.Globalization;
using System.Text;

namespace Prdb.Fab.Core.Sync;

/// <summary>
/// A sprite sheet's WebVTT, read as what it is here: a list of times and the
/// rectangle of the sheet to show at each of them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing in the file is an address.</strong> A WebVTT cue for a
/// thumbnail track is conventionally written as
/// <c>sprite.jpg#xywh=0,0,320,180</c>, and the part before the <c>#</c> is a URL
/// somebody else wrote. This reads the fragment and throws the rest away — so a
/// cue naming <c>http://elsewhere/</c>, <c>../../etc/passwd</c> or a
/// <c>data:</c> URL is not a thing this tool could be made to fetch, open or
/// pass on, because there is no code path from a cue to a request. That is a
/// property of the parser rather than a check inside it, which is why it is
/// written down here.
/// </para>
/// <para>
/// <strong>It is strict about what it accepts and forgiving about what it
/// ignores.</strong> A cue whose timing or geometry does not hold makes the
/// whole timeline unusable rather than being skipped: half a scrubbing strip is
/// worse than none, because the tile a person lands on would be the wrong
/// second and nothing would say so. Structure it does not understand — cue
/// identifiers, <c>NOTE</c> blocks, styling, settings after the timestamps — is
/// skipped, because those say nothing about which tile is when.
/// </para>
/// <para>
/// Written against the WebVTT grammar's timestamp forms rather than against a
/// library, for ADR 0036's reason on the other side of the wire: a dependency
/// is worth its weight, and what is needed here is two integers and a
/// rectangle.
/// </para>
/// </remarks>
public static class SpriteTimeline
{
    /// <summary>What a WebVTT file has to begin with.</summary>
    private const string Signature = "WEBVTT";

    /// <summary>
    /// Reads <paramref name="vtt"/> against the sheet it belongs to.
    /// </summary>
    /// <param name="vtt">The file, as bytes: it is UTF-8 by the specification.</param>
    /// <param name="sheet">
    /// The sprite's real width and height, read off the image itself rather
    /// than off the payload that described it.
    /// </param>
    /// <param name="tiles">The most cues this will accept (<see cref="UserPreviewContract.Tiles"/>).</param>
    public static SpriteTimelineResult Read(ReadOnlySpan<byte> vtt, (int Width, int Height) sheet, int tiles)
    {
        if (sheet.Width <= 0 || sheet.Height <= 0)
        {
            return SpriteTimelineResult.Unusable("The sprite sheet has no readable dimensions.");
        }

        string text;

        try
        {
            // Throwing rather than replacing, because a WebVTT that is not
            // UTF-8 is not a WebVTT, and silently turning bad bytes into
            // replacement characters would let one parse as an empty timeline.
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(TrimByteOrderMark(vtt));
        }
        catch (DecoderFallbackException)
        {
            return SpriteTimelineResult.Unusable("The WebVTT is not valid UTF-8.");
        }

        if (!text.StartsWith(Signature, StringComparison.Ordinal)
            || (text.Length > Signature.Length && text[Signature.Length] is not ('\n' or '\r' or ' ' or '\t')))
        {
            return SpriteTimelineResult.Unusable("The WebVTT does not begin with its signature.");
        }

        var cues = new List<SpriteTile>();
        var lines = text.Split('\n');
        var lastStart = TimeSpan.MinValue;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim('\r', ' ', '\t');

            if (!Timing(line, out var start, out var end))
            {
                continue;
            }

            if (end <= start)
            {
                return SpriteTimelineResult.Unusable("A cue ends before it starts.");
            }

            if (start < lastStart)
            {
                // Out of order. The strip is read by binary search over the
                // start times, and a file that is not sorted would make a
                // person's scrub land on an arbitrary tile.
                return SpriteTimelineResult.Unusable("The cues are not in order.");
            }

            lastStart = start;

            if (index + 1 >= lines.Length)
            {
                return SpriteTimelineResult.Unusable("A cue has no payload.");
            }

            if (Crop(lines[index + 1]) is not { } crop)
            {
                return SpriteTimelineResult.Unusable("A cue does not name a rectangle of the sheet.");
            }

            if (crop.Width <= 0
                || crop.Height <= 0
                || crop.X < 0
                || crop.Y < 0
                || (long)crop.X + crop.Width > sheet.Width
                || (long)crop.Y + crop.Height > sheet.Height)
            {
                return SpriteTimelineResult.Unusable("A cue names a rectangle outside the sheet.");
            }

            if (cues.Count == tiles)
            {
                return SpriteTimelineResult.Unusable("The WebVTT carries more cues than a sheet may have tiles.");
            }

            cues.Add(new SpriteTile(start, end, crop.X, crop.Y, crop.Width, crop.Height));
            index++;
        }

        return cues.Count == 0
            ? SpriteTimelineResult.Unusable("The WebVTT carries no cues.")
            : SpriteTimelineResult.Of(cues);
    }

    /// <summary>
    /// <c>hh:mm:ss.mmm --&gt; hh:mm:ss.mmm</c>, or the same without the hours.
    /// </summary>
    private static bool Timing(string line, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;

        var arrow = line.IndexOf("-->", StringComparison.Ordinal);

        if (arrow < 0)
        {
            return false;
        }

        var after = line[(arrow + 3)..].Trim();

        // Cue settings — alignment, position, the region — follow the second
        // timestamp separated by a space. They say nothing about which tile is
        // when.
        var settings = after.IndexOf(' ', StringComparison.Ordinal);

        if (settings >= 0)
        {
            after = after[..settings];
        }

        return Stamp(line[..arrow].Trim(), out start) && Stamp(after, out end);
    }

    private static bool Stamp(string value, out TimeSpan at)
    {
        at = default;

        var parts = value.Split(':');

        if (parts.Length is not (2 or 3))
        {
            return false;
        }

        var hours = 0;

        if (parts.Length == 3 && !int.TryParse(parts[0], CultureInfo.InvariantCulture, out hours))
        {
            return false;
        }

        if (!int.TryParse(parts[^2], CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[^1], CultureInfo.InvariantCulture, out var seconds)
            || hours < 0
            || minutes is < 0 or > 59
            || seconds is < 0 or >= 60)
        {
            return false;
        }

        at = new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);

        return true;
    }

    /// <summary>
    /// The <c>#xywh=x,y,w,h</c> of a cue payload, and nothing else from it.
    /// </summary>
    /// <remarks>
    /// Everything before the fragment is discarded unread. See the class
    /// remarks: that is the reason no cue can name something this tool would go
    /// and get.
    /// </remarks>
    private static (int X, int Y, int Width, int Height)? Crop(string payload)
    {
        const string Marker = "#xywh=";

        var at = payload.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return null;
        }

        var numbers = payload[(at + Marker.Length)..].Trim('\r', ' ', '\t').Split(',');

        if (numbers.Length != 4)
        {
            return null;
        }

        var values = new int[4];

        for (var index = 0; index < 4; index++)
        {
            if (!int.TryParse(numbers[index].Trim(), CultureInfo.InvariantCulture, out values[index]))
            {
                return null;
            }
        }

        return (values[0], values[1], values[2], values[3]);
    }

    private static ReadOnlySpan<byte> TrimByteOrderMark(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;
}

/// <summary>One tile of a sprite sheet: when it is, and where on the sheet.</summary>
public sealed record SpriteTile(TimeSpan Start, TimeSpan End, int X, int Y, int Width, int Height);

/// <summary>
/// What a WebVTT was worth: the tiles, or the sentence saying why there are
/// none.
/// </summary>
/// <remarks>
/// A reason rather than an exception, and a reason that is never read for
/// control flow (ADR 0016, ADR 0043). What a caller does with an unusable
/// timeline is the same whatever went wrong: the preview is not available, and
/// the gallery shows the rest.
/// </remarks>
public sealed record SpriteTimelineResult(IReadOnlyList<SpriteTile> Tiles, string? Reason)
{
    public bool Usable => Reason is null;

    public static SpriteTimelineResult Of(IReadOnlyList<SpriteTile> tiles) => new(tiles, null);

    public static SpriteTimelineResult Unusable(string reason) => new([], reason);
}
