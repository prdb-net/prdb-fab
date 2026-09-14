using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

public sealed record ContactSheetProcessResult(int ExitCode, byte[] Bytes, bool TimedOut);

public interface IContactSheetProcess
{
    Task<ContactSheetProcessResult> RunAsync(
        string path,
        long runtimeSeconds,
        CancellationToken cancellationToken);
}

/// <summary>Produces ADR 0053's one five-frame JPEG without exposing the Video File.</summary>
/// <remarks>
/// Five frames at fixed positions, forty-five seconds, one thread: ADR 0053's
/// contact sheet is unchanged by ADR 0064 having a second decode of its own.
/// What the two share is <see cref="FfmpegCapture"/> — how a subprocess is
/// started, bounded and killed — and nothing about what either asks ffmpeg for.
/// </remarks>
public sealed class FfmpegContactSheetProcess : IContactSheetProcess
{
    private const int FrameCount = 5;

    /// <summary>
    /// Far more than five tiles can weigh, and there so that a decode cannot
    /// produce unbounded output down the pipe whatever the input turns out to
    /// be. The sheet this makes is 1600 by 180 pixels.
    /// </summary>
    private const long MostBytes = 8L * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);
    private static readonly double[] Positions = [0.1, 0.3, 0.5, 0.7, 0.9];

    public async Task<ContactSheetProcessResult> RunAsync(
        string path,
        long runtimeSeconds,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error" };

        foreach (var position in Positions)
        {
            var latest = Math.Max(0, runtimeSeconds - 1);
            var second = Math.Min(runtimeSeconds * position, latest);
            arguments.Add("-ss");
            arguments.Add(second.ToString("0.###", CultureInfo.InvariantCulture));
            arguments.Add("-i");
            arguments.Add(path);
        }

        var frames = string.Join(
            ';',
            Enumerable.Range(0, FrameCount).Select(index =>
                $"[{index}:v:0]scale=320:180:force_original_aspect_ratio=increase,crop=320:180,setsar=1[frame{index}]"));
        var stack = string.Concat(Enumerable.Range(0, FrameCount).Select(index => $"[frame{index}]"));

        arguments.AddRange([
            "-filter_complex", $"{frames};{stack}hstack=inputs={FrameCount}[sheet]",
            "-map", "[sheet]",
            "-frames:v", "1",
            "-q:v", "4",
            "-threads", "1",
            "-f", "image2pipe",
            "-vcodec", "mjpeg",
            "pipe:1",
        ]);

        var run = await FfmpegCapture.RunAsync(arguments, Timeout, MostBytes, cancellationToken);

        return new ContactSheetProcessResult(run.ExitCode, run.Bytes, run.TimedOut || run.TooLarge);
    }
}

/// <summary>Reads only open Review Queue files and returns no file bytes of its own.</summary>
public sealed class ReviewFileContactSheet(
    FabDbContext context,
    IContactSheetProcess process)
{
    public async Task<byte[]?> ReadAsync(Guid arrivingFileId, CancellationToken cancellationToken)
    {
        var file = await context.ArrivingFiles
            .AsNoTracking()
            .Where(row => row.Id == arrivingFileId && row.Reason != null && row.IsOnDisk)
            .Select(row => new { row.SourcePath, row.SizeBytes, row.RuntimeSeconds })
            .SingleOrDefaultAsync(cancellationToken);
        if (file?.RuntimeSeconds is not > 0)
        {
            return null;
        }

        try
        {
            if (!File.Exists(file.SourcePath)
                || new FileInfo(file.SourcePath).Length != file.SizeBytes)
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        ContactSheetProcessResult result;
        try
        {
            result = await process.RunAsync(file.SourcePath, file.RuntimeSeconds.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }

        return !result.TimedOut && result.ExitCode == 0 && result.Bytes.Length > 0
            ? result.Bytes
            : null;
    }
}
