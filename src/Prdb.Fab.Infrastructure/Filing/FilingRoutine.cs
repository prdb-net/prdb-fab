using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Files one Arriving File, alone, and resumes from its intended path.</summary>
public sealed class FilingRoutine(
    FabDbContext context,
    EntryFiles entryFiles,
    VideoFileMover mover,
    TimeProvider time,
    ILogger<FilingRoutine> logger) : IRoutine
{
    public const string RoutineName = "Filing";

    public string Name => RoutineName;
    public Lane Lane => Lane.File;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var arrival = await context.ArrivingFiles
            .AsTracking()
            .Where(row => (row.Reason == null
                && (row.State == ArrivingFileState.Filing
                    || (row.State == ArrivingFileState.AwaitingFiling
                        && !context.ArrivingFiles.Any(other =>
                            other.Id != row.Id
                            && other.VideoId == row.VideoId
                            && other.State == ArrivingFileState.Filing))))
                || (row.Reason == ArrivingFileReason.Duplicate
                    && row.State == ArrivingFileState.Filing
                    && row.IntendedPath != null))
            .OrderBy(row => row.LastAttemptedAt != null)
            .ThenBy(row => row.LastAttemptedAt)
            .ThenBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (arrival is null)
        {
            return RunResult.NothingToDo;
        }

        arrival.LastAttemptedAt = time.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        if (arrival.Reason == ArrivingFileReason.Duplicate)
        {
            await ReplaceAsync(arrival, cancellationToken);
            logger.LogInformation("Replaced one filed Video File from the Review Queue.");
            return RunResult.Handled(1);
        }

        var root = await AvailableLibraryRootAsync(cancellationToken);

        if (arrival.State == ArrivingFileState.AwaitingFiling
            && !await PrepareAsync(arrival, root, cancellationToken))
        {
            return RunResult.Handled(1);
        }

        if (!await ExecuteAsync(arrival, cancellationToken))
        {
            return RunResult.Handled(1);
        }

        logger.LogInformation("Filed one Arriving File into the Library.");
        return RunResult.Handled(1);
    }

    private async Task ReplaceAsync(
        ArrivingFileRow arrival,
        CancellationToken cancellationToken)
    {
        var videoId = arrival.VideoId
            ?? throw new InvalidOperationException("A replacement has no Video.");
        var quality = arrival.QualityLabel
            ?? throw new InvalidOperationException("A replacement has no Quality.");
        var intended = arrival.IntendedPath
            ?? throw new InvalidOperationException("A replacement has no intended path.");
        var displaced = await context.VideoFiles
            .AsTracking()
            .SingleAsync(
                row => row.LibraryEntryVideoId == videoId && row.QualityLabel == quality,
                cancellationToken);
        var entry = await context.LibraryEntries
            .AsNoTracking()
            .SingleAsync(row => row.VideoId == videoId, cancellationToken);
        if (!string.Equals(
                Path.GetDirectoryName(intended),
                entry.EntryDirectory,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A replacement intended path is outside its Entry Directory.");
        }

        var targetIsNew = File.Exists(intended)
            && mover.Matches(intended, arrival.SizeBytes, arrival.OsHash);
        if (targetIsNew
            && arrival.OsHash is null
            && File.Exists(arrival.SourcePath))
        {
            // Equal size is not evidence that the old filed copy is the new
            // target. On a resumed replacement the target and the still-held
            // source are byte-identical; before the replacement they need not
            // be. This keeps the recovery shortcut from preserving the file
            // the person explicitly chose to replace.
            targetIsNew = await mover.SameBytesAsync(
                intended,
                arrival.SourcePath,
                cancellationToken);
        }
        if (!targetIsNew)
        {
            if (!File.Exists(arrival.SourcePath)
                || !mover.Matches(arrival.SourcePath, arrival.SizeBytes, arrival.OsHash))
            {
                throw new IOException("The replacement source is not present as it was confirmed.");
            }

            if (!string.Equals(intended, displaced.FiledPath, StringComparison.Ordinal)
                && File.Exists(intended))
            {
                throw new IOException("The replacement path is occupied by different content.");
            }

            var temporary = Path.Combine(
                entry.EntryDirectory,
                FiledPaths.TemporaryName(arrival.DownloadId));
            await mover.CopyAndVerifyAsync(arrival.SourcePath, temporary, cancellationToken);
            File.Move(temporary, intended, overwrite: true);
        }

        if (!string.Equals(displaced.FiledPath, intended, StringComparison.Ordinal)
            && File.Exists(displaced.FiledPath))
        {
            File.Delete(displaced.FiledPath);
        }

        if (File.Exists(arrival.SourcePath))
        {
            if (!mover.Matches(arrival.SourcePath, arrival.SizeBytes, arrival.OsHash))
            {
                throw new IOException("The replacement source changed after it was confirmed.");
            }

            File.Delete(arrival.SourcePath);
        }

        var before = displaced.FiledPath;
        displaced.FiledPath = intended;
        displaced.SizeBytes = arrival.SizeBytes;
        displaced.RuntimeSeconds = arrival.RuntimeSeconds;
        displaced.Width = arrival.Width;
        displaced.Height = arrival.Height;
        displaced.VideoCodec = arrival.VideoCodec;
        displaced.OsHash = arrival.OsHash;
        var now = time.GetUtcNow();
        context.OperationLogEntries.Add(new OperationLogEntryRow
        {
            Id = Guid.CreateVersion7(now),
            Act = "Replaced",
            VideoFileId = displaced.Id,
            LibraryEntryVideoId = videoId,
            VideoId = videoId,
            DownloadId = arrival.DownloadId,
            PathBefore = arrival.SourcePath,
            PathAfter = intended,
            DisplacedPath = before,
            Actor = "Person",
            Reason = "Review Queue: Replace",
            At = now,
        });
        context.ArrivingFiles.Remove(arrival);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> PrepareAsync(
        ArrivingFileRow arrival,
        string root,
        CancellationToken cancellationToken)
    {
        if (arrival.VideoId is not { } videoId || arrival.QualityLabel is not { } quality)
        {
            throw new InvalidOperationException("An Arriving File reached Filing without a Video and Quality.");
        }

        if (await IsIdenticalAsync(arrival, cancellationToken))
        {
            await StopAsync(arrival, ArrivingFileReason.IdenticalFile, cancellationToken);
            return false;
        }

        var entry = await context.LibraryEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.VideoId == videoId, cancellationToken);

        string entryDirectory;
        IReadOnlyList<VideoFileRow> held;

        if (entry is null)
        {
            var metadata = await FiledVideoAsync(videoId, cancellationToken);
            entryDirectory = ChooseEntryDirectory(root, metadata, Path.GetExtension(arrival.SourcePath));
            held = [];
        }
        else
        {
            entryDirectory = entry.EntryDirectory;
            var recorded = DirectoryAt(entryDirectory);
            if (recorded == DirectoryState.Unreadable)
            {
                throw new IOException("The recorded Entry Directory could not be read.");
            }

            if (recorded is DirectoryState.Absent or DirectoryState.NotADirectory)
            {
                await StopAsync(arrival, ArrivingFileReason.EntryMissing, cancellationToken);
                return false;
            }

            held = await PresentFilesAsync(videoId, cancellationToken);
            if (held.Any(file => string.Equals(file.QualityLabel, quality, StringComparison.Ordinal)))
            {
                await StopAsync(arrival, ArrivingFileReason.Duplicate, cancellationToken);
                return false;
            }
        }

        var secondQuality = entry is null
            ? SecondQualityVerdict.FileUnlabelled
            : FiledPaths.ForSecondQuality(
                held.Count > 0 ? RecordedEntryState.FileIsThere : RecordedEntryState.FileIsGone);
        var recordedPath = EntryPath.At(entryDirectory, Path.GetExtension(arrival.SourcePath));
        var intended = Path.Combine(
            entryDirectory,
            secondQuality == SecondQualityVerdict.RelabelThenFile
                ? recordedPath.VideoFileNameFor(quality)
                : recordedPath.VideoFileName);
        if (Inspect(intended) != Node.Missing)
        {
            throw new IOException("The Filed Path is occupied by content the tool did not put there.");
        }

        PreflightRelabels(held, intended, quality);

        arrival.IntendedPath = intended;
        arrival.State = ArrivingFileState.Filing;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> ExecuteAsync(ArrivingFileRow arrival, CancellationToken cancellationToken)
    {
        var intended = arrival.IntendedPath
            ?? throw new InvalidOperationException("A Filing Arriving File has no intended path.");
        var entryDirectory = Path.GetDirectoryName(intended)!;
        var videoId = arrival.VideoId
            ?? throw new InvalidOperationException("A Filing Arriving File has no Video.");
        var recordedEntry = await context.LibraryEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.VideoId == videoId, cancellationToken);
        if (recordedEntry is not null)
        {
            if (!string.Equals(recordedEntry.EntryDirectory, entryDirectory, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The intended path is outside the recorded Entry Directory.");
            }

            var directoryState = DirectoryAt(entryDirectory);
            if (directoryState == DirectoryState.Unreadable)
            {
                throw new IOException("The recorded Entry Directory could not be read.");
            }

            if (directoryState is DirectoryState.Absent or DirectoryState.NotADirectory)
            {
                await StopAsync(arrival, ArrivingFileReason.EntryMissing, cancellationToken);
                return false;
            }
        }

        var held = await RecordedFilesAsync(videoId, cancellationToken);
        var quality = arrival.QualityLabel
            ?? throw new InvalidOperationException("A Filing Arriving File has no Quality.");

        var targetState = Inspect(intended);
        var sourceState = Inspect(arrival.SourcePath);

        if (targetState == Node.File)
        {
            if (!mover.Matches(intended, arrival.SizeBytes, arrival.OsHash))
            {
                throw new IOException("The intended Filed Path is occupied by different content.");
            }

            if (sourceState == Node.File
                && (!mover.Matches(arrival.SourcePath, arrival.SizeBytes, arrival.OsHash)
                    || !await mover.SameBytesAsync(arrival.SourcePath, intended, cancellationToken)))
            {
                throw new IOException("The Arriving File changed after it was probed; its source was left untouched.");
            }
        }
        else if (targetState != Node.Missing)
        {
            throw new IOException("The intended Filed Path is occupied by something other than a Video File.");
        }
        else if (sourceState != Node.File)
        {
            throw new FileNotFoundException("Neither the Arriving File nor its intended Filed Path exists.");
        }
        else if (!mover.Matches(arrival.SourcePath, arrival.SizeBytes, arrival.OsHash))
        {
            throw new IOException("The Arriving File changed after it was probed; it was left untouched.");
        }

        if (targetState == Node.Missing)
        {
            if (await IsIdenticalAsync(arrival, cancellationToken))
            {
                await StopAsync(arrival, ArrivingFileReason.IdenticalFile, cancellationToken);
                return false;
            }

            if (held.Any(file =>
                    Inspect(file.FiledPath) == Node.File
                    && string.Equals(file.QualityLabel, quality, StringComparison.Ordinal)))
            {
                await StopAsync(arrival, ArrivingFileReason.Duplicate, cancellationToken);
                return false;
            }
        }

        var relabels = PreflightRelabels(held, intended, quality);

        // ADR 0026: Entry Directory, Sidecar, Entry Image, relabel, Video File.
        await entryFiles.WriteAsync(entryDirectory, videoId, cancellationToken);
        await ApplyRelabelsAsync(arrival, relabels, cancellationToken);

        if (targetState == Node.Missing)
        {
            var temporary = Path.Combine(entryDirectory, FiledPaths.TemporaryName(arrival.DownloadId));
            File.Delete(temporary);
            var move = FilingMoves.For(Directories.OnTheSameFilesystem(arrival.SourcePath, entryDirectory));
            await mover.MoveAsync(arrival.SourcePath, intended, temporary, move, cancellationToken);
        }
        else if (sourceState == Node.File)
        {
            File.Delete(arrival.SourcePath);
        }

        if (Inspect(arrival.SourcePath) != Node.Missing)
        {
            throw new IOException("The Arriving File source still exists after Filing.");
        }

        await CompleteAsync(arrival, entryDirectory, intended, cancellationToken);
        return true;
    }

    private async Task CompleteAsync(
        ArrivingFileRow arrival,
        string entryDirectory,
        string intended,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var videoId = arrival.VideoId!.Value;
        var fileId = Guid.CreateVersion7(now);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var entry = await context.LibraryEntries
            .AsTracking()
            .SingleOrDefaultAsync(row => row.VideoId == videoId, cancellationToken);
        if (entry is null)
        {
            context.LibraryEntries.Add(new LibraryEntryRow
            {
                VideoId = videoId,
                EntryDirectory = entryDirectory,
                FiledAt = now,
            });
        }
        else if (!string.Equals(entry.EntryDirectory, entryDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The recorded Entry Directory changed while Filing was in progress.");
        }

        foreach (var stale in await context.VideoFiles
                     .AsTracking()
                     .Where(row => row.LibraryEntryVideoId == videoId)
                     .ToListAsync(cancellationToken))
        {
            if (Inspect(stale.FiledPath) != Node.File)
            {
                context.VideoFiles.Remove(stale);
            }
        }

        context.VideoFiles.Add(new VideoFileRow
        {
            Id = fileId,
            LibraryEntryVideoId = videoId,
            FiledPath = intended,
            QualityLabel = arrival.QualityLabel!,
            SizeBytes = arrival.SizeBytes,
            RuntimeSeconds = arrival.RuntimeSeconds,
            Width = arrival.Width,
            Height = arrival.Height,
            VideoCodec = arrival.VideoCodec,
            OsHash = arrival.OsHash,
        });
        context.OperationLogEntries.Add(new OperationLogEntryRow
        {
            Id = Guid.CreateVersion7(now),
            Act = "Filed",
            VideoFileId = fileId,
            LibraryEntryVideoId = videoId,
            VideoId = videoId,
            DownloadId = arrival.DownloadId,
            PathBefore = arrival.SourcePath,
            PathAfter = intended,
            Actor = RoutineName,
            Reason = arrival.Confidence is { } confidence
                ? $"{confidence} Identification"
                : "Confirmed Identification",
            At = now,
        });

        arrival.State = ArrivingFileState.Filed;
        arrival.IsOnDisk = false;
        arrival.Reason = null;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<FiledVideo> FiledVideoAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await context.CatalogueVideos
            .AsNoTracking()
            .Include(row => row.Site)
            .SingleAsync(row => row.PrdbId == videoId, cancellationToken);

        return new FiledVideo(
            videoId,
            video.Site?.Title ?? throw new InvalidOperationException("A filing Catalogue Video has no Site."),
            video.Title,
            video.ReleaseDate);
    }

    private async Task<bool> IsIdenticalAsync(
        ArrivingFileRow arrival,
        CancellationToken cancellationToken)
    {
        if (arrival.OsHash is not { } hash)
        {
            return false;
        }

        var matches = await context.VideoFiles
            .AsNoTracking()
            .Where(row => row.OsHash == hash)
            .ToListAsync(cancellationToken);

        return matches.Any(row =>
            Inspect(row.FiledPath) == Node.File
            && mover.Matches(row.FiledPath, row.SizeBytes, row.OsHash));
    }

    private async Task<IReadOnlyList<VideoFileRow>> PresentFilesAsync(
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var rows = await context.VideoFiles
            .AsTracking()
            .Where(row => row.LibraryEntryVideoId == videoId)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return rows.Where(row => Inspect(row.FiledPath) == Node.File).ToList();
    }

    private Task<List<VideoFileRow>> RecordedFilesAsync(
        Guid videoId,
        CancellationToken cancellationToken) =>
        context.VideoFiles
            .AsTracking()
            .Where(row => row.LibraryEntryVideoId == videoId)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

    private IReadOnlyList<Relabel> PreflightRelabels(
        IReadOnlyList<VideoFileRow> held,
        string intended,
        string arrivingQuality)
    {
        if (!CarriesQualityLabel(intended, arrivingQuality))
        {
            return [];
        }

        var relabels = new List<Relabel>();
        foreach (var file in held.Where(file =>
                     !CarriesQualityLabel(file.FiledPath, file.QualityLabel)))
        {
            var path = EntryPath.At(
                Path.GetDirectoryName(file.FiledPath)!,
                Path.GetExtension(file.FiledPath));
            var after = Path.Combine(Path.GetDirectoryName(file.FiledPath)!, path.VideoFileNameFor(file.QualityLabel));
            var beforeState = Inspect(file.FiledPath);
            var afterState = Inspect(after);

            if (beforeState == Node.File && afterState == Node.Missing)
            {
                relabels.Add(new Relabel(file, file.FiledPath, after, Move: true));
            }
            else if (beforeState == Node.Missing
                     && afterState == Node.File
                     && mover.Matches(after, file.SizeBytes, file.OsHash))
            {
                relabels.Add(new Relabel(file, file.FiledPath, after, Move: false));
            }
            else if (beforeState == Node.File || afterState != Node.Missing)
            {
                throw new IOException("A second-Quality relabel would collide with existing content.");
            }
        }

        return relabels;
    }

    private async Task ApplyRelabelsAsync(
        ArrivingFileRow arrival,
        IReadOnlyList<Relabel> relabels,
        CancellationToken cancellationToken)
    {
        foreach (var relabel in relabels)
        {
            if (relabel.Move)
            {
                File.Move(relabel.Before, relabel.After);
            }

            relabel.File.FiledPath = relabel.After;
            context.OperationLogEntries.Add(new OperationLogEntryRow
            {
                Id = Guid.CreateVersion7(time.GetUtcNow()),
                Act = "Relabelled",
                VideoFileId = relabel.File.Id,
                LibraryEntryVideoId = relabel.File.LibraryEntryVideoId,
                VideoId = relabel.File.LibraryEntryVideoId,
                DownloadId = arrival.DownloadId,
                PathBefore = relabel.Before,
                PathAfter = relabel.After,
                Actor = RoutineName,
                Reason = "Second Quality",
                At = time.GetUtcNow(),
            });

            // The relabel is a complete act even when the newcomer later fails.
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static string ChooseEntryDirectory(string root, FiledVideo video, string extension)
    {
        var ordinary = EntryPaths.For(video, extension);
        var distinguished = EntryPaths.For(video, extension, distinguish: true);
        return FiledPaths.For(
                DirectoryAt(ordinary.DirectoryUnder(root)),
                DirectoryAt(distinguished.DirectoryUnder(root))) switch
            {
                EntryDirectoryVerdict.Use => ordinary.DirectoryUnder(root),
                EntryDirectoryVerdict.Distinguish => distinguished.DirectoryUnder(root),
                _ => throw new IOException("The computed Entry Directory could not be used safely."),
            };
    }

    private async Task<string> AvailableLibraryRootAsync(CancellationToken cancellationToken)
    {
        var root = await context.Installation
            .AsNoTracking()
            .Select(row => row.LibraryRoot)
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(root)
            || DirectoryAt(root) is DirectoryState.Absent or DirectoryState.NotADirectory or DirectoryState.Unreadable
            || !Directories.IsReadable(root)
            || !Directories.IsWritable(root))
        {
            throw new IOException("The Library root is not present, readable and writable.");
        }

        return root;
    }

    private async Task StopAsync(
        ArrivingFileRow arrival,
        ArrivingFileReason reason,
        CancellationToken cancellationToken)
    {
        if (arrival.State == ArrivingFileState.Filing)
        {
            arrival.State = ArrivingFileState.AwaitingFiling;
            arrival.IntendedPath = null;
        }

        arrival.Reason = reason;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Node Inspect(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                return Node.File;
            }

            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return entries.MoveNext() ? Node.OccupiedDirectory : Node.EmptyDirectory;
        }
        catch (FileNotFoundException)
        {
            return Node.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return Node.Missing;
        }
    }

    private static DirectoryState DirectoryAt(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                return DirectoryState.NotADirectory;
            }

            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            return entries.MoveNext() ? DirectoryState.OccupiedDirectory : DirectoryState.EmptyDirectory;
        }
        catch (FileNotFoundException)
        {
            return DirectoryState.Absent;
        }
        catch (DirectoryNotFoundException)
        {
            return DirectoryState.Absent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DirectoryState.Unreadable;
        }
    }

    private static bool CarriesQualityLabel(string filedPath, string qualityLabel) =>
        Path.GetFileNameWithoutExtension(filedPath)
            .EndsWith($" - [{qualityLabel}]", StringComparison.Ordinal);

    private sealed record Relabel(VideoFileRow File, string Before, string After, bool Move);

    private enum Node
    {
        Missing,
        File,
        EmptyDirectory,
        OccupiedDirectory,
    }
}
