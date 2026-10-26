using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

public sealed class CollectionUndo(FabDbContext context, TimeProvider time, ContentMutationLock content)
{
    private IQueryable<CollectionUndoCandidate> Candidates() =>
        (from collection in context.CollectionFiles
         join root in context.CollectionRoots on collection.RootId equals root.Id
         join operation in context.OperationLogEntries on collection.SourcePath equals operation.PathBefore
         join file in context.VideoFiles on operation.VideoFileId equals file.Id
         join entry in context.LibraryEntries on file.LibraryEntryVideoId equals entry.VideoId
         where collection.SourcePath != file.FiledPath && collection.LeftAt == null && operation.Act == "Filed" && operation.DownloadId == null
             && !context.CollectionUndos.Any(undo => undo.CollectionFileId == collection.Id)
         select new CollectionUndoCandidate
         {
             CollectionFileId = collection.Id, VideoFileId = file.Id, VideoId = file.LibraryEntryVideoId,
             SourcePath = file.FiledPath, DestinationPath = collection.SourcePath, SourceRoot = entry.EntryDirectory,
             DestinationRoot = root.Path, SizeBytes = file.SizeBytes, OsHash = file.OsHash,
         }).Distinct();

    public async Task<CollectionUndoPreview> ReadAsync(int page, CancellationToken token)
    {
        var preparation = await context.CollectionPreparations.SingleOrDefaultAsync(token);
        var wanted = Paging.Wanted(page);
        var candidates = Candidates();
        var rows = await candidates.OrderBy(row => row.DestinationPath).Skip(Paging.Skip(wanted, 50)).Take(50).ToListAsync(token);
        var files = rows.Select(row => new CollectionUndoPreviewFile(row.CollectionFileId, row.SourcePath,
            row.DestinationPath, row.SizeBytes, Conflict(row))).ToArray();
        var progress = await context.CollectionUndos.OrderByDescending(row => row.Error != null)
            .ThenBy(row => row.CompletedAt != null).ThenBy(row => row.Id)
            .Skip(Paging.Skip(wanted, 50)).Take(50)
            .Select(row => new CollectionUndoProgress(row.Id, row.SourcePath, row.DestinationPath,
                row.CompletedAt != null ? "Returned" : !row.Approved ? "Awaiting confirmation after Restore" : row.Error != null ? "Waiting" : "Pending", row.Error))
            .ToListAsync(token);
        return new(preparation?.Revision ?? 0, await EvidenceAsync(token), preparation?.Phase ?? "NotStarted", wanted, 50,
            await candidates.CountAsync(token), files,
            await context.CollectionUndos.CountAsync(token),
            await context.CollectionUndos.CountAsync(row => row.CompletedAt != null, token), progress);
    }

    public async Task<CollectionAct> RequestAsync(long revision, string evidence, IReadOnlyCollection<Guid> ids, bool all, CancellationToken token)
    {
        if (ids.Count > 200) return new(false, "Select at most 200 files, or confirm the whole preview.");
        using var lease = await content.EnterAsync(token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var preparation = await context.CollectionPreparations.AsTracking().SingleOrDefaultAsync(token);
        if (preparation is null || preparation.Revision != revision || preparation.Phase is not ("Ready" or "UndoComplete"))
            return new(false, "Finish the import and refresh the return preview before confirming.");
        if (evidence != await EvidenceAsync(token))
            return new(false, "The return paths or Library records changed. Refresh the preview before approving.");
        var selected = await Candidates().Where(row => all || ids.Contains(row.CollectionFileId)).ToListAsync(token);
        if (selected.Count == 0 || !all && selected.Count != ids.Distinct().Count())
            return new(false, "The selected files changed. Refresh the return preview.");
        if (selected.Select(row => row.CollectionFileId).Distinct().Count() != selected.Count)
            return new(false, "More than one held file points to the same original path. Resolve the conflicting Library records before approving a return.");
        var now = time.GetUtcNow();
        context.CollectionUndos.AddRange(selected.Select(row => new CollectionUndoRow
        {
            Id = Guid.CreateVersion7(now), CollectionFileId = row.CollectionFileId,
            VideoFileId = row.VideoFileId, VideoId = row.VideoId, SourcePath = row.SourcePath,
            DestinationPath = row.DestinationPath, SourceRoot = row.SourceRoot, DestinationRoot = row.DestinationRoot,
            SizeBytes = row.SizeBytes, OsHash = row.OsHash, RequestedAt = now, Approved = true,
        }));
        preparation.Phase = "Undoing";
        preparation.Error = null;
        preparation.Revision++;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, $"{selected.Count} returns approved. Each path and file is checked before moving. Automatic acquisition waits until you explicitly resume it.");
    }

    public async Task<CollectionAct> ResumeAsync(bool confirmRestoredReturns, CancellationToken token)
    {
        using var lease = await content.EnterAsync(token);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var preparation = await context.CollectionPreparations.AsTracking().SingleOrDefaultAsync(token);
        if (preparation is null) return new(false, "No collection preparation exists.");
        if (confirmRestoredReturns)
        {
            if (!await context.CollectionUndos.AnyAsync(row => row.CompletedAt == null && !row.Approved, token))
                return new(false, "No restored returns await confirmation.");
            await context.CollectionUndos.Where(row => row.CompletedAt == null).ExecuteUpdateAsync(update => update.SetProperty(row => row.Approved, true), token);
            preparation.Phase = "Undoing";
        }
        else
        {
            if (preparation.Phase != "UndoComplete" || await context.CollectionUndos.AnyAsync(row => row.CompletedAt == null, token))
                return new(false, "All approved returns must finish before acquisition resumes.");
            preparation.Phase = "Ready";
        }
        preparation.Revision++;
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return new(true, confirmRestoredReturns ? "Restored return paths confirmed; processing will resume." : "Automatic acquisition may resume. Returned Videos are no longer held and may be downloaded again if Wanted and permitted.");
    }

    private async Task<string> EvidenceAsync(CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await foreach (var row in Candidates().OrderBy(row => row.CollectionFileId).ThenBy(row => row.VideoFileId).AsAsyncEnumerable().WithCancellation(token))
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(row));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string? Conflict(CollectionUndoCandidate row)
    {
        try
        {
            CollectionPaths.RequireDirectory(row.DestinationRoot, row.DestinationRoot);
            CollectionPaths.RequireSafeDestination(row.DestinationPath, row.DestinationRoot);
            CollectionPaths.RequireContained(row.SourcePath, row.SourceRoot);
            if (File.Exists(row.DestinationPath) || Directory.Exists(row.DestinationPath)) return "The original path is occupied. Nothing will be overwritten.";
            if (!File.Exists(row.SourcePath) || new FileInfo(row.SourcePath).Length != row.SizeBytes) return "The Library file is missing or changed.";
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "A path is unavailable or contains a symbolic link. Restore access before returning this file.";
        }
    }
}

public sealed record CollectionUndoCandidate
{
    public Guid CollectionFileId { get; init; }
    public Guid VideoFileId { get; init; }
    public Guid VideoId { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string SourceRoot { get; init; }
    public required string DestinationRoot { get; init; }
    public long SizeBytes { get; init; }
    public string? OsHash { get; init; }
}
public sealed record CollectionUndoPreviewFile(Guid Id, string SourcePath, string DestinationPath, long SizeBytes, string? Conflict);
public sealed record CollectionUndoProgress(Guid Id, string SourcePath, string DestinationPath, string State, string? Error);
public sealed record CollectionUndoPreview(long Revision, string Evidence, string Phase, int Page, int PageSize, int Available,
    IReadOnlyList<CollectionUndoPreviewFile> Files, int Requested, int Completed, IReadOnlyList<CollectionUndoProgress> Progress);
