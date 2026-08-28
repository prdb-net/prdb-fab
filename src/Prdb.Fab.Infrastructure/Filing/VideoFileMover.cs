using Prdb.Fab.Core.Filing;
using Prdb.Hashing;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Performs and freshly verifies ADR 0026's two move executions.</summary>
public sealed class VideoFileMover
{
    public async Task MoveAsync(
        string source,
        string target,
        string temporary,
        FilingMove move,
        CancellationToken cancellationToken)
    {
        if (move == FilingMove.Rename)
        {
            try
            {
                File.Move(source, target);
                return;
            }
            catch (IOException exception) when (IsCrossDevice(exception))
            {
                // Linux normally tells Directories the devices up front. If
                // mountinfo was unavailable, the kernel's EXDEV is the same
                // answer arriving here and takes the already verified branch.
            }
        }

        File.Delete(temporary);

        try
        {
            await using (var input = OpenRead(source))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (!await SameBytesAsync(source, temporary, cancellationToken))
            {
                throw new IOException("The copied Video File did not verify against its source.");
            }

            File.Move(temporary, target);
            File.Delete(source);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    public async Task<bool> SameBytesAsync(
        string first,
        string second,
        CancellationToken cancellationToken)
    {
        var firstSize = new FileInfo(first).Length;
        var secondSize = new FileInfo(second).Length;
        if (firstSize != secondSize)
        {
            return false;
        }

        if (OsHash.TryCompute(first, out var firstHash)
            && OsHash.TryCompute(second, out var secondHash))
        {
            return string.Equals(firstHash, secondHash, StringComparison.OrdinalIgnoreCase);
        }

        await using var one = OpenRead(first);
        await using var other = OpenRead(second);
        var left = new byte[64 * 1024];
        var right = new byte[left.Length];

        while (true)
        {
            var leftCount = await one.ReadAsync(left, cancellationToken);
            var rightCount = await other.ReadAsync(right, cancellationToken);
            if (leftCount != rightCount)
            {
                return false;
            }

            if (leftCount == 0)
            {
                return true;
            }

            if (!left.AsSpan(0, leftCount).SequenceEqual(right.AsSpan(0, rightCount)))
            {
                return false;
            }
        }
    }

    public bool Matches(string path, long sizeBytes, string? osHash)
    {
        var file = new FileInfo(path);
        if (file.Length != sizeBytes)
        {
            return false;
        }

        return osHash is null
            || (OsHash.TryCompute(path, out var found)
                && string.Equals(found, osHash, StringComparison.OrdinalIgnoreCase));
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 1024 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static bool IsCrossDevice(IOException exception) =>
        (exception.HResult & 0xffff) == 18;
}
