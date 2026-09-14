using System.Globalization;
using System.Text;

namespace Prdb.Fab.Core.Sync;

/// <summary>
/// Everything about one generated Sprite Sheet that follows from a Runtime: how
/// many tiles, in what grid, taken from where, and the WebVTT that says so.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic of <see cref="PreviewPublicationContract"/> gathered into one
/// value, because the decode, the size check and the WebVTT all have to agree
/// about it and three callers computing it separately is three chances to
/// disagree. Nothing here reads a file: ADR 0064 computes the whole plan from
/// the Runtime the Probe already recorded, which is ADR 0021's rule and
/// ADR 0061's amendment to it applied again.
/// </para>
/// <para>
/// <strong>It is the other direction of <see cref="SpriteTimeline"/>.</strong>
/// That reads somebody else's WebVTT against a sheet this tool did not make;
/// this writes one for a sheet it did. They meet in the test that reads back
/// what this wrote — which is the check worth having, because a strip whose cue
/// times do not land on the frames that were captured is wrong in the one way
/// nothing on screen would show.
/// </para>
/// </remarks>
public sealed record SpriteSheetPlan
{
    private SpriteSheetPlan(TimeSpan runtime, int tiles, int columns, int rows)
    {
        Runtime = runtime;
        Tiles = tiles;
        Columns = columns;
        Rows = rows;
    }

    /// <summary>The Runtime the plan was cut from, as the Probe recorded it.</summary>
    public TimeSpan Runtime { get; }

    /// <summary>How many tiles the sheet carries.</summary>
    public int Tiles { get; }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>
    /// How wide the finished sheet has to be, to the pixel.
    /// </summary>
    /// <remarks>
    /// An expectation rather than a description. The grid is always filled —
    /// ffmpeg's <c>tile</c> pads a short last row with its background colour —
    /// so a sheet that comes back a different size is a decode that did
    /// something other than what was asked, and the pair is refused rather than
    /// published.
    /// </remarks>
    public int Width => Columns * PreviewPublicationContract.TileWidth;

    public int Height => Rows * PreviewPublicationContract.TileHeight;

    /// <summary>How much of the file one tile stands for.</summary>
    public TimeSpan Interval => Runtime / Tiles;

    /// <summary>
    /// Where the first frame is taken from, which is half an interval in.
    /// </summary>
    /// <remarks>
    /// The middle of the stretch a tile stands for rather than its start. A
    /// strip sampled at <c>0</c> opens on the black frame most files begin
    /// with, and the tile a person lands on is meant to be representative of
    /// the seconds around it rather than of the instant it starts.
    /// </remarks>
    public TimeSpan FirstFrameAt => PreviewPublicationContract.TileAt(0, Tiles, Runtime);

    /// <summary>The plan for a file of this Runtime.</summary>
    public static SpriteSheetPlan For(TimeSpan runtime)
    {
        var tiles = PreviewPublicationContract.TilesFor(runtime);

        return new SpriteSheetPlan(
            runtime,
            tiles,
            PreviewPublicationContract.ColumnsFor(tiles),
            PreviewPublicationContract.RowsFor(tiles));
    }

    /// <summary>Where in the file the tile at <paramref name="index"/> comes from.</summary>
    public TimeSpan FrameAt(int index) =>
        PreviewPublicationContract.TileAt(index, Tiles, Runtime);

    /// <summary>Where on the sheet the tile at <paramref name="index"/> sits.</summary>
    public (int X, int Y) TileOrigin(int index) =>
        (index % Columns * PreviewPublicationContract.TileWidth,
            index / Columns * PreviewPublicationContract.TileHeight);

    /// <summary>
    /// The WebVTT for this sheet, as the bytes that are sent.
    /// </summary>
    /// <param name="sheetName">
    /// What the cues name before the fragment. It is written because the
    /// convention writes it and read by nobody — <see cref="SpriteTimeline"/>
    /// discards everything before the <c>#</c> — so it is a fixed string rather
    /// than anything derived from the Video File.
    /// </param>
    /// <remarks>
    /// <para>
    /// Cue <em>n</em> covers <c>[n, n+1)</c> intervals of the Runtime and shows
    /// the frame taken from the middle of it, so the boundaries are computed
    /// from the Runtime rather than accumulated from the interval: the last cue
    /// then ends exactly at the Runtime instead of a rounding error short of
    /// it.
    /// </para>
    /// <para>
    /// UTF-8 without a byte order mark. The parser accepts one and the
    /// specification allows it; not writing one keeps the bytes the smallest
    /// thing that is still a WebVTT.
    /// </para>
    /// </remarks>
    public byte[] Vtt(string sheetName)
    {
        var vtt = new StringBuilder("WEBVTT\n");

        for (var index = 0; index < Tiles; index++)
        {
            var (x, y) = TileOrigin(index);

            vtt.Append('\n')
                .Append(Stamp(Runtime * ((double)index / Tiles)))
                .Append(" --> ")
                .Append(Stamp(Runtime * ((double)(index + 1) / Tiles)))
                .Append('\n')
                .Append(sheetName)
                .Append("#xywh=")
                .Append(x.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(y.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(PreviewPublicationContract.TileWidth.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(PreviewPublicationContract.TileHeight.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(vtt.ToString());
    }

    /// <summary>
    /// <c>hh:mm:ss.mmm</c>, which is the form with hours — the only one that is
    /// unambiguous for a file over an hour long, and legal for one that is not.
    /// </summary>
    private static string Stamp(TimeSpan at) =>
        $"{(int)at.TotalHours:00}:{at.Minutes:00}:{at.Seconds:00}.{at.Milliseconds:000}";
}
