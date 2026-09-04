using System.Diagnostics;
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
public sealed class FfmpegContactSheetProcess : IContactSheetProcess
{
    private const int FrameCount = 5;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);
    private static readonly double[] Positions = [0.1, 0.3, 0.5, 0.7, 0.9];

    public async Task<ContactSheetProcessResult> RunAsync(
        string path,
        long runtimeSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        foreach (var position in Positions)
        {
            var latest = Math.Max(0, runtimeSeconds - 1);
            var second = Math.Min(runtimeSeconds * position, latest);
            process.StartInfo.ArgumentList.Add("-ss");
            process.StartInfo.ArgumentList.Add(second.ToString("0.###", CultureInfo.InvariantCulture));
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(path);
        }

        var frames = string.Join(
            ';',
            Enumerable.Range(0, FrameCount).Select(index =>
                $"[{index}:v:0]scale=320:180:force_original_aspect_ratio=increase,crop=320:180,setsar=1[frame{index}]"));
        var stack = string.Concat(Enumerable.Range(0, FrameCount).Select(index => $"[frame{index}]"));
        process.StartInfo.ArgumentList.Add("-filter_complex");
        process.StartInfo.ArgumentList.Add($"{frames};{stack}hstack=inputs={FrameCount}[sheet]");
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("[sheet]");
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-q:v");
        process.StartInfo.ArgumentList.Add("4");
        process.StartInfo.ArgumentList.Add("-threads");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("image2pipe");
        process.StartInfo.ArgumentList.Add("-vcodec");
        process.StartInfo.ArgumentList.Add("mjpeg");
        process.StartInfo.ArgumentList.Add("pipe:1");

        process.Start();
        await using var bytes = new MemoryStream();
        var output = process.StandardOutput.BaseStream.CopyToAsync(bytes, cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
            return new ContactSheetProcessResult(process.ExitCode, bytes.ToArray(), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            return new ContactSheetProcessResult(-1, [], true);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }
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
