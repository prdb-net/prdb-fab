using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Returns one approved file per turn, with full-byte recovery evidence committed before moving.</summary>
public sealed class CollectionUndoRoutine(FabDbContext context, VideoFileMover mover,
    ContentMutationLock content, TimeProvider time) : IRoutine
{
    public const string RoutineName = "Collection return";
    public string Name => RoutineName;
    public Lane Lane => Lane.File;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        using var lease = await content.EnterAsync(cancellationToken);
        var now = time.GetUtcNow();
        var row = await context.CollectionUndos.AsTracking()
            .Where(row => row.Approved && row.CompletedAt == null
                && (row.AttemptedAt == null || row.AttemptedAt <= now.AddMinutes(-1)))
            .OrderBy(row => row.AttemptedAt).ThenBy(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        if (row is null) return RunResult.NothingToDo;
        row.AttemptedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            CollectionPaths.RequireDirectory(row.DestinationRoot, row.DestinationRoot);
            CollectionPaths.RequireSafeDestination(row.DestinationPath, row.DestinationRoot);
            CollectionPaths.RequireSafeDestination(row.SourcePath, row.SourceRoot);
            var file = await context.VideoFiles.AsTracking().SingleOrDefaultAsync(file => file.Id == row.VideoFileId, cancellationToken);
            if (file is null || file.FiledPath != row.SourcePath || file.SizeBytes != row.SizeBytes || file.OsHash != row.OsHash)
                throw new CollectionReturnRefusedException("The approved Library record changed. The return is waiting for the original record.");

            if (row.StartedAt is null)
            {
                if (File.Exists(row.DestinationPath) || Directory.Exists(row.DestinationPath))
                    throw new CollectionReturnRefusedException("The original path is occupied. Move the conflicting file yourself; nothing will be overwritten.");
                CollectionPaths.RequireContained(row.SourcePath, row.SourceRoot);
                if (!mover.Matches(row.SourcePath, row.SizeBytes, row.OsHash))
                    throw new CollectionReturnRefusedException("The Library file changed after the return was approved.");
                row.ContentHash = await HashAsync(row.SourcePath, cancellationToken);
                row.StartedAt = now;
                // The evidence survives a crash after rename or source deletion.
                await context.SaveChangesAsync(cancellationToken);
            }

            if (File.Exists(row.DestinationPath))
            {
                if (await HashAsync(row.DestinationPath, cancellationToken) != row.ContentHash)
                    throw new CollectionReturnRefusedException("The return path contains different content. Nothing will be overwritten.");
                if (File.Exists(row.SourcePath))
                {
                    if (await HashAsync(row.SourcePath, cancellationToken) != row.ContentHash
                        || !await mover.SameBytesAsync(row.SourcePath, row.DestinationPath, cancellationToken))
                        throw new CollectionReturnRefusedException("The interrupted return has differing source and destination content.");
                    File.Delete(row.SourcePath);
                }
            }
            else
            {
                if (Directory.Exists(row.DestinationPath)) throw new CollectionReturnRefusedException("A directory occupies the return path.");
                if (!File.Exists(row.SourcePath) || await HashAsync(row.SourcePath, cancellationToken) != row.ContentHash)
                    throw new CollectionReturnRefusedException("The approved content is no longer at the Library path.");
                Directory.CreateDirectory(Path.GetDirectoryName(row.DestinationPath)!);
                CollectionPaths.RequireSafeDestination(row.DestinationPath, row.DestinationRoot);
                await mover.MoveAsync(row.SourcePath, row.DestinationPath,
                    Path.Combine(Path.GetDirectoryName(row.DestinationPath)!, $".return-{row.Id:N}.part"),
                    FilingMoves.For(Directories.OnTheSameFilesystem(row.SourcePath, row.DestinationPath)), cancellationToken);
            }
            if (File.Exists(row.SourcePath) || await HashAsync(row.DestinationPath, cancellationToken) != row.ContentHash)
                throw new CollectionReturnRefusedException("The returned file could not be verified. Its Library record is retained.");

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            context.VideoFiles.Remove(file);
            var lastFile = !await context.VideoFiles.AnyAsync(other => other.LibraryEntryVideoId == row.VideoId && other.Id != file.Id, cancellationToken);
            if (lastFile)
            {
                var entry = await context.LibraryEntries.AsTracking().SingleAsync(entry => entry.VideoId == row.VideoId, cancellationToken);
                context.LibraryEntries.Remove(entry);
            }
            var collectionFile = await context.CollectionFiles.AsTracking().SingleAsync(item => item.Id == row.CollectionFileId, cancellationToken);
            collectionFile.LeftAt = now;
            collectionFile.ApprovedAt = null;
            await context.ArrivingFiles.Where(item => item.Id == row.CollectionFileId).ExecuteDeleteAsync(cancellationToken);
            row.CompletedAt = now;
            row.Error = null;
            context.OperationLogEntries.Add(new OperationLogEntryRow
            {
                Id = Guid.CreateVersion7(now), Act = "Returned", VideoFileId = row.VideoFileId,
                VideoId = row.VideoId, PathBefore = row.SourcePath, PathAfter = row.DestinationPath,
                Actor = "Person", Reason = "Approved collection import return", At = now,
            });
            if (!await context.CollectionUndos.AnyAsync(other => other.Id != row.Id && other.CompletedAt == null, cancellationToken))
            {
                var preparation = await context.CollectionPreparations.AsTracking().SingleAsync(cancellationToken);
                preparation.Phase = "UndoComplete";
                preparation.Error = null;
                preparation.Revision++;
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (lastFile) CleanGeneratedFiles(row.SourceRoot);
            return RunResult.Handled(1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Only our fixed messages are safe to expose. Filesystem exceptions
            // can contain private paths or host-specific implementation details.
            row.Error = error is InsufficientStorageException or CollectionReturnRefusedException ? error.Message
                : "The return is waiting: check the source, original destination, permissions and conflicting files. No different content will be overwritten. It retries automatically.";
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Failed(row.Error);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    private static void CleanGeneratedFiles(string directory)
    {
        try
        {
            File.Delete(Path.Combine(directory, EntryPath.SidecarFileName));
            File.Delete(Path.Combine(directory, EntryPath.EntryImageFileName));
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Generated companions are disposable. The content return is already recorded.
        }
    }
}

internal sealed class CollectionReturnRefusedException(string message) : IOException(message);
