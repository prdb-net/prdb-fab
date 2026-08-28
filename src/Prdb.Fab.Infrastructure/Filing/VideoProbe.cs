using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Prdb.Fab.Core.Filing;
using Prdb.Hashing;

namespace Prdb.Fab.Infrastructure.Filing;

public sealed record ProbeProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

public interface IProbeProcess
{
    Task<ProbeProcessResult> RunAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Runs the one bounded ffprobe command used for arrival evidence.</summary>
public sealed class FfprobeProcess : IProbeProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<ProbeProcessResult> RunAsync(string path, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-select_streams");
        process.StartInfo.ArgumentList.Add("v");
        process.StartInfo.ArgumentList.Add("-show_entries");
        process.StartInfo.ArgumentList.Add("format=duration:stream=width,height,codec_name:stream_disposition=attached_pic");
        process.StartInfo.ArgumentList.Add("-of");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add(path);

        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProbeProcessResult(process.ExitCode, await output, await error, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new ProbeProcessResult(-1, await output, await error, true);
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

public sealed record VideoProbeReading(
    ProbeOutcome Outcome,
    long SizeBytes,
    long? RuntimeSeconds,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? QualityLabel,
    string? OsHash,
    string? Error);

/// <summary>Reads the six durable probe values and the canonical prdb OS hash once.</summary>
public sealed class VideoProbe(IProbeProcess process)
{
    public async Task<VideoProbeReading> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Failed(ProbeOutcome.SourceMissing, "The source file disappeared before it could be read.");
        }

        long size;
        string? osHash = null;
        try
        {
            size = new FileInfo(path).Length;
            if (OsHash.TryCompute(path, out var computed))
            {
                osHash = computed;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(ProbeOutcome.Unreadable, "The source file could not be read.");
        }

        ProbeProcessResult result;
        try
        {
            result = await process.RunAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return Failed(ProbeOutcome.Unreadable, "ffprobe could not be started.", size, osHash);
        }

        if (result.TimedOut)
        {
            return Failed(ProbeOutcome.TimedOut, "ffprobe did not finish within 30 seconds.", size, osHash);
        }

        if (result.ExitCode != 0)
        {
            return Failed(ProbeOutcome.Unreadable, ShortError(result.StandardError), size, osHash);
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var stream = root.TryGetProperty("streams", out var streams)
                ? streams.EnumerateArray().FirstOrDefault(IsReadableVideo)
                : default;

            if (stream.ValueKind == JsonValueKind.Undefined)
            {
                return Failed(ProbeOutcome.NoVideoStream, "ffprobe found no video stream.", size, osHash);
            }

            var width = Number(stream, "width");
            var height = Number(stream, "height");
            var codec = Text(stream, "codec_name");
            var seconds = Duration(root);

            return new VideoProbeReading(
                ProbeOutcome.Read,
                size,
                seconds,
                width,
                height,
                codec,
                width is { } w && height is { } h ? VideoQuality.LabelFor(w, h) : null,
                osHash,
                null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Failed(ProbeOutcome.Unreadable, "ffprobe returned unreadable JSON.", size, osHash);
        }
    }

    private static bool IsReadableVideo(JsonElement stream) =>
        Number(stream, "width") is > 0
        && Number(stream, "height") is > 0
        && (!stream.TryGetProperty("disposition", out var disposition)
            || !disposition.TryGetProperty("attached_pic", out var attached)
            || attached.GetInt32() == 0);

    private static int? Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var found) && found.TryGetInt32(out var number) ? number : null;

    private static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var found) ? found.GetString() : null;

    private static long? Duration(JsonElement root)
    {
        if (!root.TryGetProperty("format", out var format)
            || !format.TryGetProperty("duration", out var duration))
        {
            return null;
        }

        var text = duration.ValueKind == JsonValueKind.String
            ? duration.GetString()
            : duration.GetRawText();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? checked((long)Math.Round(seconds, MidpointRounding.AwayFromZero))
            : null;
    }

    private static VideoProbeReading Failed(
        ProbeOutcome outcome,
        string? error,
        long size = 0,
        string? osHash = null) =>
        new(outcome, size, null, null, null, null, null, osHash, error);

    private static string ShortError(string error)
    {
        var oneLine = error.ReplaceLineEndings(" ").Trim();
        return oneLine.Length switch
        {
            0 => "ffprobe could not read the file.",
            > 240 => oneLine[..240],
            _ => oneLine,
        };
    }
}
