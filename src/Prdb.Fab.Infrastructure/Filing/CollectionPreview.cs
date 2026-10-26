using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Connections;

namespace Prdb.Fab.Infrastructure.Filing;

public sealed class CollectionPreview(FabDbContext context, TimeProvider time)
{
    public const int PageSize = 50;

    public async Task<CollectionPreviewPage> ReadAsync(int page, CancellationToken cancellationToken = default)
    {
        var preparation = await context.CollectionPreparations.SingleOrDefaultAsync(cancellationToken);
        var files = context.CollectionFiles.AsNoTracking();
        var wanted = Paging.Wanted(page);
        var records = await files.OrderBy(row => row.SourcePath).Skip(Paging.Skip(wanted, PageSize)).Take(PageSize).ToListAsync(cancellationToken);
        var ids = records.Select(row => row.Id).ToArray();
        var arrivals = await context.ArrivingFiles.Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        var videos = arrivals.Values.Select(row => row.VideoId).OfType<Guid>().ToArray();
        var titles = await context.CatalogueVideos.Where(row => videos.Contains(row.PrdbId))
            .ToDictionaryAsync(row => row.PrdbId, row => row.Title, cancellationToken);
        var entries = records.Select(file =>
        {
            var arrival = arrivals.GetValueOrDefault(file.Id);
            return new CollectionPreviewFile(file.Id, file.SourcePath, file.SizeBytes,
                arrival?.VideoId, arrival?.VideoId is { } video ? titles.GetValueOrDefault(video) : null,
                arrival?.Confidence, arrival?.QualityLabel, file.PlannedPath,
                file.LeftAt != null ? "Left in place" : arrival?.State == ArrivingFileState.Filed ? "Filed"
                    : file.ApprovedAt != null ? "Approved" : file.PreviewDetail ?? "Waiting for preparation",
                file.ApprovedAt != null, file.LeftAt != null);
        }).ToArray();
        var library = await context.Installation.Select(row => row.LibraryRoot).SingleAsync(cancellationToken);
        var roots = await context.CollectionRoots.ToDictionaryAsync(row => row.Id, row => row.Path, cancellationToken);
        var sizes = await files.Where(row => row.LeftAt == null && row.PlannedPath != null
                && !context.ArrivingFiles.Any(arrival => arrival.Id == row.Id && arrival.State == ArrivingFileState.Filed))
            .GroupBy(row => row.RootId).Select(group => new { Root = group.Key, Bytes = group.Sum(row => row.SizeBytes) })
            .ToListAsync(cancellationToken);
        var required = sizes.Where(row => library == null || !roots.TryGetValue(row.Root, out var source)
                || Directories.OnTheSameFilesystem(source, library) != true).Sum(row => row.Bytes);
        var available = library == null ? null : StorageSpace.AvailableAt(library);
        var space = new CollectionStorageSpace(required, available, StorageSpace.IsInsufficient(required, available));
        return new(preparation?.Phase ?? "NotStarted", preparation?.Revision ?? 0, preparation?.Error,
            wanted, PageSize, await files.CountAsync(cancellationToken),
            await files.CountAsync(row => row.PreviewEvidence != null && row.PlannedPath != null && row.LeftAt == null, cancellationToken),
            await files.CountAsync(row => row.Probed && row.PreviewEvidence != null && row.PlannedPath == null && row.LeftAt == null, cancellationToken),
            await files.CountAsync(row => row.ApprovedAt != null && row.LeftAt == null, cancellationToken),
            await files.CountAsync(row => row.LeftAt != null, cancellationToken), entries, space);
    }

    public async Task<CollectionAct> ApproveAsync(long revision, IReadOnlyCollection<Guid> moveIds,
        IReadOnlyCollection<Guid> leaveIds, bool allReady, bool leaveUnresolved, CancellationToken cancellationToken = default)
    {
        if (moveIds.Count + leaveIds.Count > 200 || moveIds.Intersect(leaveIds).Any())
            return new(false, "Choose at most 200 distinct files per selection, or use the whole-preview action.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var preparation = await context.CollectionPreparations.AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (preparation is null || preparation.Revision != revision || preparation.Phase != "Preview" || preparation.Error != null)
            return new(false, "The preview changed or preparation is incomplete. Review the current preview.");
        var files = await context.CollectionFiles.AsTracking().Where(row => row.LeftAt == null
            && !context.ArrivingFiles.Any(arrival => arrival.Id == row.Id && arrival.State == ArrivingFileState.Filed)).ToListAsync(cancellationToken);
        var ids = files.Select(row => row.Id).ToArray();
        var arrivals = await context.ArrivingFiles.Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
        if (files.Any(file => !arrivals.TryGetValue(file.Id, out var arrival)
            || file.PreviewEvidence != CollectionEvidence.Of(file, arrival)))
        {
            foreach (var file in files) file.PreviewEvidence = null;
            preparation.Phase = "Planning";
            preparation.Revision++;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(false, "Identification changed. An updated preview is being prepared; nothing was approved.");
        }
        var moving = files.Where(file => (allReady && file.PlannedPath != null) || moveIds.Contains(file.Id)).ToArray();
        var leaving = files.Where(file => (leaveUnresolved && file.PlannedPath == null) || leaveIds.Contains(file.Id)).ToArray();
        if (moveIds.Concat(leaveIds).Any(id => !ids.Contains(id)) || moving.Any(file => file.PlannedPath == null)
            || moving.Select(file => file.Id).Intersect(leaving.Select(file => file.Id)).Any())
            return new(false, "The selection changed. Only files with a prepared destination may be imported.");
        if (moving.Length + leaving.Length == 0) return new(false, "Select files to import or explicitly leave in place.");
        var now = time.GetUtcNow();
        foreach (var file in moving)
        {
            file.ApprovedAt = now;
            file.ApprovedEvidence = file.PreviewEvidence;
            file.ValidatedAt = null;
        }
        foreach (var file in leaving)
        {
            file.LeftAt = now;
            file.ApprovedAt = null;
            // This is an explicit dismissal, including any candidate holds.
            await context.ArrivingFiles.Where(row => row.Id == file.Id).ExecuteDeleteAsync(cancellationToken);
        }
        if (files.All(file => file.LeftAt != null || file.ApprovedAt != null))
        {
            preparation.Phase = "Validating";
            await context.CollectionWalks.ExecuteUpdateAsync(update => update.SetProperty(row => row.Verified, false), cancellationToken);
        }
        preparation.Revision++;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, preparation.Phase == "Validating"
            ? "The selection is recorded. Sources and destinations will be checked before any file moves."
            : "The selection is recorded. Decide the remaining files before import starts.");
    }
}

internal static class CollectionEvidence
{
    public static string Of(CollectionFileRow file, ArrivingFileRow arrival) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            file.SourcePath, file.SizeBytes, file.LastWriteAt, file.PlannedPath,
            arrival.VideoId, arrival.QualityLabel, arrival.OsHash, arrival.Confidence,
            arrival.Reason,
        }))));
}

public sealed record CollectionPreviewFile(Guid Id, string SourcePath, long SizeBytes, Guid? VideoId,
    string? Title, IdentificationConfidence? Confidence, string? Quality, string? Destination,
    string Detail, bool Approved, bool Left);
public sealed record CollectionPreviewPage(string Phase, long Revision, string? Error, int Page, int PageSize,
    int Total, int Ready, int Unresolved, int Approved, int Left, IReadOnlyList<CollectionPreviewFile> Files, CollectionStorageSpace Storage);
public sealed record CollectionStorageSpace(long RequiredCopyBytes, long? AvailableBytes, bool Insufficient);
