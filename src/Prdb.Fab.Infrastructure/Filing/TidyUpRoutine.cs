using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Deletes only fixed Leftovers from one decided Download directory.</summary>
public sealed class TidyUpRoutine(
    FabDbContext context,
    TimeProvider time,
    ILogger<TidyUpRoutine> logger) : IRoutine
{
    public const string RoutineName = "Tidy up Download Directories";

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromMinutes(1);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var download = await context.Downloads
            .AsTracking()
            .Where(row => row.State == DownloadState.Collected
                && row.TidiedAt == null
                && !context.ArrivingFiles.Any(file =>
                    file.DownloadId == row.Id && file.State != ArrivingFileState.Filed))
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (download is null)
        {
            return RunResult.NothingToDo;
        }

        var settings = await context.Installation
            .AsNoTracking()
            .Select(row => new { row.PathMappingFrom, row.PathMappingTo, row.DeleteLeftovers })
            .SingleAsync(cancellationToken);
        var storage = PathMapping.Resolve(
            settings.PathMappingFrom,
            settings.PathMappingTo,
            download.Storage);

        if (storage is null)
        {
            throw new IOException("The collected Download path does not fall under the configured Path Mapping.");
        }

        var removed = new List<string>();

        if (settings.DeleteLeftovers && Directory.Exists(storage))
        {
            removed.AddRange(DeleteLeftovers(storage));
            RemoveEmptyDirectories(storage);
        }

        // A single-file storage path is deliberately not turned into its parent.
        // A missing directory is likewise already tidy.
        var now = time.GetUtcNow();
        download.TidiedAt = now;

        if (removed.Count > 0)
        {
            context.OperationLogEntries.Add(new OperationLogEntryRow
            {
                Id = Guid.CreateVersion7(now),
                Act = "Tidied",
                DownloadId = download.Id,
                PathBefore = storage,
                LeftoverNamesJson = JsonSerializer.Serialize(removed),
                Actor = RoutineName,
                Reason = "Leftover deletion setting",
                At = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Tidied one collected Download directory and removed {Count} Leftover(s).",
            removed.Count);
        return RunResult.Handled(1);
    }

    private static IReadOnlyList<string> DeleteLeftovers(string root)
    {
        var removed = new List<string>();
        foreach (var path in WalkFiles(root))
        {
            if (!Leftovers.IsSupported(Path.GetFileName(path)))
            {
                continue;
            }

            File.Delete(path);
            removed.Add(path);
        }

        removed.Sort(StringComparer.Ordinal);
        return removed;
    }

    private static IEnumerable<string> WalkFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var info = new DirectoryInfo(child);
                if (VideoFiles.IsWorthWalking(info.Name)
                    && !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root)
                     .SelectMany(DescendantsAndSelf)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(root).Any())
        {
            Directory.Delete(root);
        }
    }

    private static IEnumerable<string> DescendantsAndSelf(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var info = new DirectoryInfo(child);
            if (VideoFiles.IsWorthWalking(info.Name)
                && !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var descendant in DescendantsAndSelf(child))
                {
                    yield return descendant;
                }
            }
        }

        yield return directory;
    }
}
