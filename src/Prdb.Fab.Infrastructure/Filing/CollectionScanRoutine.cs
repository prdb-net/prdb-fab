using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Bounded enumeration and probing; Identification uses the ordinary arrival work set.</summary>
public sealed class CollectionScanRoutine(FabDbContext context, VideoProbe probe, TimeProvider time) : IRoutine
{
    public const string RoutineName = "Collection preparation";
    public const int EntriesPerTurn = 200;
    public const int ProbesPerTurn = 5;
    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var preparation = await context.CollectionPreparations.AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (preparation is null || preparation.Phase is "Ready" or "Importing" or "Preview" or "Planning" or "Validating") return RunResult.NothingToDo;
        var roots = await context.CollectionRoots.ToListAsync(cancellationToken);
        if (roots.Count == 0) return RunResult.Failed("Collection preparation has no Scan Directories.");
        try
        {
            // Restore deliberately drops enumeration positions.
            foreach (var root in roots)
            {
                CollectionPaths.RequireDirectory(root.Path, root.Path);
                if (!await context.CollectionWalks.AnyAsync(row => row.Path == root.Path, cancellationToken))
                    context.CollectionWalks.Add(new() { Path = root.Path, RootId = root.Id });
            }
            await context.SaveChangesAsync(cancellationToken);
            var walk = await context.CollectionWalks.AsTracking().Where(row => !row.Completed)
                .OrderBy(row => row.Path).FirstOrDefaultAsync(cancellationToken);
            if (walk is not null)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                preparation.Phase = "Inventory";
                var root = roots.Single(row => row.Id == walk.RootId);
                await EnumerateAsync(walk, root, roots, preparation, cancellationToken);
                preparation.Error = null;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return RunResult.Handled(1);
            }

            var files = await context.CollectionFiles.AsTracking()
                .Where(row => !row.Probed && row.LeftAt == null)
                .OrderBy(row => row.Id).Take(ProbesPerTurn).ToListAsync(cancellationToken);
            if (files.Count > 0)
            {
                preparation.Phase = "Probing";
                foreach (var file in files)
                {
                    var root = roots.Single(row => row.Id == file.RootId);
                    // A missing individual file is a reviewable exception. A
                    // missing/unreadable directory remains an incomplete inventory.
                    CollectionPaths.RequireDirectory(Path.GetDirectoryName(file.SourcePath)!, root.Path);
                    if (!File.Exists(file.SourcePath))
                    {
                        context.ArrivingFiles.Add(new()
                        {
                            Id = file.Id, SourcePath = file.SourcePath, ArrivedName = Path.GetFileName(file.SourcePath),
                            SizeBytes = file.SizeBytes, State = ArrivingFileState.AwaitingIdentification,
                            IsOnDisk = false, ProbeOutcome = ProbeOutcome.SourceMissing,
                            ProbeError = "The source file disappeared before it could be read.", Reason = ArrivingFileReason.UnreadableQuality,
                        });
                        file.Probed = true;
                        preparation.Revision++;
                        await context.SaveChangesAsync(cancellationToken);
                        continue;
                    }
                    CollectionPaths.RequireContained(file.SourcePath, root.Path);
                    var before = new FileInfo(file.SourcePath);
                    var size = before.Length;
                    var stamp = before.LastWriteTimeUtc;
                    var reading = await probe.ReadAsync(file.SourcePath, cancellationToken);
                    var after = new FileInfo(file.SourcePath);
                    if (!after.Exists || after.Length != size || after.LastWriteTimeUtc != stamp)
                        throw new IOException("A source file changed while being probed; it will be retried.");
                    file.SizeBytes = size;
                    file.LastWriteAt = new DateTimeOffset(stamp, TimeSpan.Zero);
                    context.ArrivingFiles.Add(new()
                    {
                        Id = file.Id, SourcePath = file.SourcePath, ArrivedName = Path.GetFileName(file.SourcePath),
                        State = ArrivingFileState.AwaitingIdentification, SizeBytes = reading.SizeBytes,
                        RuntimeSeconds = reading.RuntimeSeconds, Width = reading.Width, Height = reading.Height,
                        VideoCodec = reading.VideoCodec, QualityLabel = reading.QualityLabel, OsHash = reading.OsHash,
                        ProbeOutcome = reading.Outcome, ProbeError = reading.Error,
                        Reason = reading.QualityLabel is null ? ArrivingFileReason.UnreadableQuality : null,
                    });
                    file.Probed = true;
                    preparation.Revision++;
                    await context.SaveChangesAsync(cancellationToken);
                }
                return RunResult.Handled(files.Count);
            }

            if (await context.ArrivingFiles.AnyAsync(row => row.DownloadId == null
                && row.State == ArrivingFileState.AwaitingIdentification && row.Reason == null
                && context.CollectionFiles.Any(file => file.Id == row.Id && file.LeftAt == null), cancellationToken))
            {
                preparation.Phase = "Identifying";
            }
            else
            {
                // Check all directory stamps before declaring the inventory complete.
                // A page inserted behind a persisted cursor must restart that directory.
                var changed = false;
                foreach (var directory in await context.CollectionWalks.AsTracking().Where(row => !row.Verified)
                             .OrderBy(row => row.Path).Take(EntriesPerTurn).ToListAsync(cancellationToken))
                {
                    CollectionPaths.RequireDirectory(directory.Path, roots.Single(row => row.Id == directory.RootId).Path);
                    directory.Verified = true;
                    if (directory.LastWriteAt != new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory.Path), TimeSpan.Zero))
                    {
                        directory.Completed = false;
                        directory.AfterName = null;
                        changed = true;
                    }
                }
                await context.SaveChangesAsync(cancellationToken);
                var verificationPending = await context.VideoFiles.AnyAsync(file => !context.LibraryVerifications.Any(answer => answer.VideoFileId == file.Id), cancellationToken);
                preparation.Phase = changed || await context.CollectionWalks.AnyAsync(row => !row.Verified, cancellationToken) ? "Inventory"
                    : await context.CollectionFiles.AnyAsync(row => row.LeftAt == null, cancellationToken) ? "Planning" : verificationPending ? "Inventory" : "Ready";
            }
            preparation.Error = null;
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Handled(1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            context.ChangeTracker.Clear();
            preparation = await context.CollectionPreparations.AsTracking().SingleAsync(cancellationToken);
            preparation.Error = "A collection path is missing, changed or unreadable. Restore access to continue.";
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Failed(preparation.Error);
        }
    }

    private async Task EnumerateAsync(CollectionWalkRow walk, CollectionRootRow root,
        IReadOnlyList<CollectionRootRow> roots, CollectionPreparationRow preparation, CancellationToken cancellationToken)
    {
        CollectionPaths.RequireDirectory(walk.Path, root.Path);
        var stamp = new DateTimeOffset(Directory.GetLastWriteTimeUtc(walk.Path), TimeSpan.Zero);
        if (walk.LastWriteAt != stamp) walk.AfterName = null;
        var downloads = await context.Installation.Select(row => row.PathMappingTo).SingleAsync(cancellationToken);
        var entries = new DirectoryInfo(walk.Path).EnumerateFileSystemInfos()
            .Where(entry => walk.AfterName == null || string.CompareOrdinal(entry.Name, walk.AfterName) > 0)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal).Take(EntriesPerTurn).ToArray();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            walk.AfterName = entry.Name;
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var path = entry.FullName;
            if (downloads is { Length: > 0 } && CollectionPaths.Within(path, downloads)) continue;
            if (entry.Attributes.HasFlag(FileAttributes.Directory))
            {
                if (!VideoFiles.IsWorthWalking(entry.Name) || roots.Any(other => other.Id != root.Id && other.Path == path)) continue;
                if (!await context.CollectionWalks.AnyAsync(row => row.Path == path, cancellationToken))
                    context.CollectionWalks.Add(new() { Path = path, RootId = root.Id });
                continue;
            }
            if (!VideoFiles.IsSupported(entry.Name)
                || await context.VideoFiles.AnyAsync(row => row.FiledPath == path, cancellationToken)
                || await context.ArrivingFiles.AnyAsync(row => (row.DownloadId != null && row.SourcePath == path)
                    || row.IntendedPath == path, cancellationToken)) continue;
            var file = (FileInfo)entry;
            var held = await context.CollectionFiles.AsTracking().SingleOrDefaultAsync(row => row.SourcePath == path, cancellationToken);
            if (held is null)
            {
                context.CollectionFiles.Add(new()
                {
                    Id = Guid.CreateVersion7(time.GetUtcNow()), RootId = root.Id, SourcePath = path,
                    SizeBytes = file.Length, LastWriteAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
                });
                preparation.Revision++;
            }
            else if (held.LeftAt == null && (held.SizeBytes != file.Length || held.LastWriteAt.UtcDateTime != file.LastWriteTimeUtc))
            {
                await context.ArrivingFileCandidates.Where(row => row.ArrivingFileId == held.Id).ExecuteDeleteAsync(cancellationToken);
                await context.ArrivingFiles.Where(row => row.Id == held.Id).ExecuteDeleteAsync(cancellationToken);
                held.Probed = false;
                held.ApprovedAt = null;
                held.ApprovedEvidence = null;
                held.PlannedPath = null;
                held.PreviewEvidence = null;
                held.PreviewDetail = null;
                held.ValidatedAt = null;
                held.SizeBytes = file.Length;
                held.LastWriteAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                preparation.Revision++;
            }
        }
        walk.Verified = false;
        walk.LastWriteAt = stamp;
        walk.Completed = entries.Length < EntriesPerTurn;
    }
}
