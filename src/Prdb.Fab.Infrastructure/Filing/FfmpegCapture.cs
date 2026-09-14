using System.Diagnostics;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>What one bounded ffmpeg run produced.</summary>
/// <param name="TimedOut">
/// The run was still going when its deadline passed and was killed. The bytes
/// are empty: a truncated picture is not a shorter picture.
/// </param>
/// <param name="TooLarge">
/// The run produced more than it was allowed to and was killed. Distinct from
/// <paramref name="TimedOut"/> because it says something different about the
/// input — a file whose sheet does not fit is not a file that is slow.
/// </param>
public sealed record FfmpegCaptureResult(
    int ExitCode,
    byte[] Bytes,
    bool TimedOut,
    bool TooLarge,
    string Error)
{
    /// <summary>Whether the run finished, on its own, inside its bounds.</summary>
    public bool Completed => !TimedOut && !TooLarge && ExitCode == 0 && Bytes.Length > 0;
}

/// <summary>
/// Runs ffmpeg over a local file and captures one picture from its standard
/// output, under a deadline and a size ceiling.
/// </summary>
/// <remarks>
/// <para>
/// The process handling ADR 0053's contact sheet established, made shared when
/// ADR 0064 needed a second decode of the same kind: the arguments go in a list
/// rather than a command line, so nothing about a path can be read as an
/// option; the picture comes back down a pipe rather than through a temporary
/// file; and a run that overruns is killed with its whole process tree rather
/// than waited for.
/// </para>
/// <para>
/// <strong>The size ceiling is read as it arrives</strong> rather than checked
/// afterwards, which is the difference between a bound and a complaint: a
/// decode that would produce a gigabyte is stopped at the ceiling instead of
/// after it. Because the reader stops reading, the process is killed rather
/// than left to block on a full pipe.
/// </para>
/// <para>
/// <strong>Cancellation and the deadline are told apart deliberately.</strong>
/// A container shutting down is not a file that could not be read: the first
/// rethrows so the lane's run is recorded as interrupted, and the second comes
/// back as a result the caller decides about.
/// </para>
/// </remarks>
public static class FfmpegCapture
{
    public static async Task<FfmpegCaptureResult> RunAsync(
        IEnumerable<string> arguments,
        TimeSpan timeout,
        long mostBytes,
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

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(timeout);

        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, mostBytes, deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            var read = await output;

            if (read.TooLarge)
            {
                // Nothing is reading the pipe any more, so the process would
                // sit in a write for as long as it was left to.
                await StopAsync(process);

                return new FfmpegCaptureResult(-1, [], TimedOut: false, TooLarge: true, Error: string.Empty);
            }

            await process.WaitForExitAsync(deadline.Token);

            return new FfmpegCaptureResult(
                process.ExitCode,
                read.Bytes,
                TimedOut: false,
                TooLarge: false,
                await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopAsync(process);

            return new FfmpegCaptureResult(-1, [], TimedOut: true, TooLarge: false, Error: string.Empty);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);

            throw;
        }
    }

    /// <summary>
    /// Reads the pipe until it ends or until one byte more than
    /// <paramref name="mostBytes"/> has arrived.
    /// </summary>
    private static async Task<(byte[] Bytes, bool TooLarge)> ReadBoundedAsync(
        Stream output,
        long mostBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];

        using var bytes = new MemoryStream();

        while (true)
        {
            var read = await output.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return (bytes.ToArray(), false);
            }

            if (bytes.Length + read > mostBytes)
            {
                return ([], true);
            }

            bytes.Write(buffer, 0, read);
        }
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception failed) when (failed is InvalidOperationException or NotSupportedException)
        {
            // It exited between the question and the answer, which is the
            // outcome this was asking for.
        }
    }
}
