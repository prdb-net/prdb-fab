using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>
/// Cuts one filed Video File into the Sprite Sheet ADR 0064 publishes.
/// </summary>
public interface ISpriteSheetProcess
{
    Task<FfmpegCaptureResult> RunAsync(string path, SpriteSheetPlan plan, CancellationToken cancellationToken);
}

/// <summary>
/// ADR 0064's later decode: one ffmpeg run over the whole file, sampling it at
/// the plan's interval and laying the frames out as a grid.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One process for the whole sheet, unlike the Review contact
/// sheet.</strong> That one seeks to five positions and opens five inputs,
/// which is right for five frames and impossible for four hundred: a run with
/// four hundred inputs would hold four hundred demuxers open. So this opens the
/// file once and walks it, which is also what makes the cost proportional to
/// the file rather than to the tile count.
/// </para>
/// <para>
/// <strong>The sampling is a rational number, not a decimal.</strong>
/// <c>fps=tiles/seconds</c> is exactly the plan's interval, because both are
/// integers — the tile count is computed from a Runtime the Probe recorded in
/// whole seconds. Written as <c>1/12.5</c> it would be an expression ffmpeg
/// evaluates in floating point, and the four hundredth frame would land
/// somewhere its cue does not say.
/// </para>
/// <para>
/// <strong>There is no seek, and that is deliberate.</strong> The tile that
/// stands for a stretch of a file should be taken from the middle of it rather
/// than from the black frame most files open on, and the obvious way to arrange
/// that is to start the walk half an interval in. It is also wrong: the
/// <c>fps</c> filter already lands there, because it keeps the last frame it
/// saw for an output slot and emits it when the next one belongs to the slot
/// after — so seeking first would move every tile a further half interval on.
/// Measured against a file with its own timestamps burned into it: with a seek,
/// the first tile of a three-minute file was 7.48 s in; without, 3.72 s, which
/// is <see cref="SpriteSheetPlan.FrameAt"/>'s 3.75 s to the frame.
/// </para>
/// <para>
/// <strong>The correctness of the cues does not rest on that</strong>, which is
/// worth saying because it rests on a filter's internals. A cue covers the
/// whole interval its tile stands for, so a frame taken anywhere inside it is
/// the right frame; where in the interval it comes from decides how good the
/// strip looks rather than whether it is true.
/// </para>
/// <para>
/// <strong>Aspect ratio is handled the way the Review sheet handles it</strong>
/// — scaled until it covers the tile and centre-cropped to it — so an unusual
/// aspect ratio produces a tile of exactly the contracted size rather than a
/// sheet whose geometry nothing can predict. A letterboxed source keeps its
/// bars; cropping them would mean deciding what is letterbox and what is
/// picture, which is a decode reading a file to decide something (ADR 0021).
/// </para>
/// <para>
/// <strong>Nothing but the picture is read.</strong> Audio, subtitle and data
/// streams are dropped at the input, and the video stream is named explicitly
/// rather than chosen — a container carrying cover art as a second video stream
/// is ordinary, and a sheet of four hundred copies of one thumbnail is the
/// failure that would follow from letting ffmpeg pick.
/// </para>
/// </remarks>
public sealed class FfmpegSpriteSheetProcess : ISpriteSheetProcess
{
    public Task<FfmpegCaptureResult> RunAsync(
        string path,
        SpriteSheetPlan plan,
        CancellationToken cancellationToken) =>
        FfmpegCapture.RunAsync(
            Arguments(path, plan),
            PreviewPublicationContract.Generation,
            PreviewPublicationContract.ASheet,
            cancellationToken);

    /// <summary>
    /// What ffmpeg is asked for, as the list it is handed.
    /// </summary>
    /// <remarks>
    /// Separable from the run so that the one thing worth checking without a
    /// decoder — that the sampling and the grid are the plan's — can be checked
    /// by reading it.
    /// </remarks>
    public static IReadOnlyList<string> Arguments(string path, SpriteSheetPlan plan)
    {
        var seconds = (long)Math.Round(plan.Runtime.TotalSeconds);
        var tile = FormattableString.Invariant(
            $"{PreviewPublicationContract.TileWidth}:{PreviewPublicationContract.TileHeight}");

        var filter = string.Join(
            ',',
            FormattableString.Invariant($"fps={plan.Tiles}/{seconds}"),
            $"scale={tile}:force_original_aspect_ratio=increase",
            $"crop={tile}",
            "setsar=1",
            FormattableString.Invariant($"tile={plan.Columns}x{plan.Rows}:margin=0:padding=0:color=black"));

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",

            "-i", path,

            "-an", "-sn", "-dn",
            "-map", "0:v:0",

            "-vf", filter,

            // One picture, whatever the arithmetic did at the end of the file:
            // the tile filter flushes a short last grid at end of stream, and a
            // frame more than the grid holds would otherwise start a second.
            "-frames:v", "1",
            "-q:v", "4",
            "-threads", "1",
            "-f", "image2pipe",
            "-vcodec", "mjpeg",
            "pipe:1",
        ];
    }
}
