using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>A person's confirmed removal of one complete Library Entry.</summary>
public sealed class LibraryEntryDeletion(
    FabDbContext context,
    TimeProvider time,
    ILogger<LibraryEntryDeletion> logger)
{
    public async Task<LibraryEntryDeletePreview?> PreviewAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var entry = await context.LibraryEntries
            .AsNoTracking()
            .Where(row => row.VideoId == videoId)
            .Select(row => new { row.VideoId, row.EntryDirectory })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var rows = await context.VideoFiles
            .AsNoTracking()
            .Where(row => row.LibraryEntryVideoId == videoId)
            .OrderBy(row => row.FiledPath)
            .Select(row => new { row.Id, row.FiledPath, row.QualityLabel, row.SizeBytes })
            .ToListAsync(cancellationToken);
        var files = rows.Select(row => new LibraryEntryDeleteFile(
            row.Id,
            Path.GetFileName(row.FiledPath),
            row.FiledPath,
            row.QualityLabel,
            row.SizeBytes)).ToList();

        var ready = files.Count > 0;
        return new LibraryEntryDeletePreview(
            ready ? LibraryEntryDeleteOutcome.Ready : LibraryEntryDeleteOutcome.EntryChanged,
            videoId,
            entry.EntryDirectory,
            files,
            ready
                ? "Every Video File in the Library Entry is named for confirmation."
                : "The Library Entry no longer contains a Video File; nothing can be confirmed for deletion.");
    }

    public async Task<LibraryEntryDeleteVerdict?> DeleteAsync(
        Guid videoId,
        IReadOnlyCollection<Guid> videoFileIds,
        CancellationToken cancellationToken = default)
    {
        var expected = videoFileIds.Distinct().ToHashSet();
        var entry = await context.LibraryEntries
            .AsTracking()
            .SingleOrDefaultAsync(row => row.VideoId == videoId, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var files = await context.VideoFiles
            .AsTracking()
            .Where(row => row.LibraryEntryVideoId == videoId)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (files.Count == 0
            || expected.Count != files.Count
            || files.Any(file => !expected.Contains(file.Id)))
        {
            return Changed("The Library Entry changed after it was shown; nothing was deleted.");
        }

        foreach (var file in files)
        {
            if (!File.Exists(file.FiledPath)
                || new FileInfo(file.FiledPath).Length != file.SizeBytes)
            {
                return Changed(
                    "A Video File is no longer present at the confirmed size; nothing was deleted.");
            }
        }

        foreach (var file in files)
        {
            File.Delete(file.FiledPath);
            var now = time.GetUtcNow();
            context.OperationLogEntries.Add(new OperationLogEntryRow
            {
                Id = Guid.CreateVersion7(now),
                VideoFileId = file.Id,
                LibraryEntryVideoId = videoId,
                VideoId = videoId,
                Act = "Deleted",
                PathBefore = file.FiledPath,
                Actor = "Person",
                Reason = "Library Entry deleted",
                At = now,
            });
            context.VideoFiles.Remove(file);
            await context.SaveChangesAsync(cancellationToken);
        }

        DeleteIfPresent(Path.Combine(entry.EntryDirectory, EntryPath.SidecarFileName));
        DeleteIfPresent(Path.Combine(entry.EntryDirectory, EntryPath.EntryImageFileName));
        DeleteIfEmpty(entry.EntryDirectory);

        context.LibraryEntries.Remove(entry);
        await context.SaveChangesAsync(cancellationToken);

        return new LibraryEntryDeleteVerdict(
            LibraryEntryDeleteOutcome.Deleted,
            files.Count,
            $"Deleted the Library Entry and {files.Count} confirmed Video File(s). Every deletion was recorded in the Operation Log.");
    }

    private static LibraryEntryDeleteVerdict Changed(string detail) =>
        new(LibraryEntryDeleteOutcome.EntryChanged, 0, detail);

    private void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The destructive content act is already complete and recorded.
            // A generated companion must not turn that success into a retry
            // that can only discover the Video Files are now gone.
            logger.LogWarning(exception, "A generated Library Entry file could not be removed from {Path}.", path);
        }
    }

    private void DeleteIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The empty Entry Directory could not be removed from {Path}.", path);
        }
    }
}

public enum LibraryEntryDeleteOutcome
{
    Ready,
    EntryChanged,
    Deleted,
}

public sealed record LibraryEntryDeleteFile(
    Guid Id,
    string FileName,
    string Path,
    string Quality,
    long SizeBytes);

public sealed record LibraryEntryDeletePreview(
    LibraryEntryDeleteOutcome Outcome,
    Guid VideoId,
    string EntryDirectory,
    IReadOnlyList<LibraryEntryDeleteFile> Files,
    string Detail);

public sealed record LibraryEntryDeleteVerdict(
    LibraryEntryDeleteOutcome Outcome,
    int DeletedFiles,
    string Detail);
