using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Revalidates the complete approved inventory before opening the serial Filing lane.</summary>
public sealed class CollectionImportRoutine(FabDbContext context, FilingRoutine filing, VideoFileMover mover, TimeProvider time) : IRoutine
{
    public const string RoutineName = "Collection import";
    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var preparation = await context.CollectionPreparations.AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (preparation?.Phase == "Preview" && !await context.CollectionFiles.AnyAsync(file => file.LeftAt == null && file.ApprovedAt == null
            && !context.ArrivingFiles.Any(arrival => arrival.Id == file.Id && arrival.State == ArrivingFileState.Filed), cancellationToken))
        {
            preparation.Phase = "Validating";
            await context.CollectionWalks.ExecuteUpdateAsync(update => update.SetProperty(row => row.Verified, false), cancellationToken);
        }
        if (preparation?.Phase is not ("Validating" or "Importing")) return RunResult.NothingToDo;
        var roots = await context.CollectionRoots.ToDictionaryAsync(row => row.Id, cancellationToken);
        try
        {
            foreach (var root in roots.Values) CollectionPaths.RequireDirectory(root.Path, root.Path);
            if (preparation.Phase == "Validating")
            {
                foreach (var walk in await context.CollectionWalks.AsTracking().Where(row => !row.Verified)
                    .OrderBy(row => row.Path).Take(200).ToListAsync(cancellationToken))
                {
                    CollectionPaths.RequireDirectory(walk.Path, roots[walk.RootId].Path);
                    if (!walk.Completed || walk.LastWriteAt != new DateTimeOffset(Directory.GetLastWriteTimeUtc(walk.Path), TimeSpan.Zero))
                    {
                        await CollectionChanges.RescanAsync(context, "The directory inventory changed. Review the updated preview before import.", cancellationToken);
                        return RunResult.Handled(1);
                    }
                    walk.Verified = true;
                }
                await context.SaveChangesAsync(cancellationToken);
                if (await context.CollectionWalks.AnyAsync(row => !row.Verified, cancellationToken)) return RunResult.Handled(1);
                var library = await context.Installation.Select(row => row.LibraryRoot).SingleAsync(cancellationToken);
                foreach (var file in await context.CollectionFiles.AsTracking().Where(row => row.LeftAt == null && row.ValidatedAt == null)
                    .OrderBy(row => row.Id).Take(5).ToListAsync(cancellationToken))
                {
                    var arrival = await context.ArrivingFiles.SingleOrDefaultAsync(row => row.Id == file.Id, cancellationToken);
                    if (arrival?.State == ArrivingFileState.Filed)
                    {
                        var held = await context.VideoFiles.Where(row => row.LibraryEntryVideoId == arrival.VideoId && row.QualityLabel == arrival.QualityLabel).ToListAsync(cancellationToken);
                        if (!held.Any(row => File.Exists(row.FiledPath) && mover.Matches(row.FiledPath, row.SizeBytes, row.OsHash)))
                            throw new IOException("A previously imported file could not be verified in the Library.");
                        file.ValidatedAt = time.GetUtcNow();
                        continue;
                    }
                    if (arrival is null || file.ApprovedAt == null || file.ApprovedEvidence != CollectionEvidence.Of(file, arrival))
                    {
                        await CollectionChanges.ReplanAsync(context, file, "Identification changed. Review the updated preview.", cancellationToken);
                        return RunResult.Handled(1);
                    }
                    if (arrival.State == ArrivingFileState.Filing && arrival.IntendedPath == file.PlannedPath)
                    {
                        CollectionPaths.RequireSafeDestination(file.PlannedPath!, library!);
                        var targetMatches = File.Exists(file.PlannedPath) && mover.Matches(file.PlannedPath!, arrival.SizeBytes, arrival.OsHash);
                        if (File.Exists(file.SourcePath))
                        {
                            CollectionPaths.RequireContained(file.SourcePath, roots[file.RootId].Path);
                            if (!CollectionChanges.SourceMatches(file, arrival, mover))
                                throw new IOException("The source of an interrupted move changed.");
                        }
                        else if (!targetMatches) throw new IOException("An interrupted move has neither its verified source nor target.");
                        file.ValidatedAt = time.GetUtcNow();
                        continue;
                    }
                    CollectionPaths.RequireContained(file.SourcePath, roots[file.RootId].Path);
                    if (!CollectionChanges.SourceMatches(file, arrival, mover))
                    {
                        await CollectionChanges.RescanAsync(context, "A source changed. Its new content requires a new preview.", cancellationToken);
                        return RunResult.Handled(1);
                    }
                    var plan = await filing.PlanAsync(arrival, library!, cancellationToken);
                    if (plan.Path != file.PlannedPath || plan.Reason != null)
                    {
                        await CollectionChanges.ReplanAsync(context, file, "A destination changed. Review the updated preview.", cancellationToken);
                        return RunResult.Handled(1);
                    }
                    file.ValidatedAt = time.GetUtcNow();
                }
                await context.SaveChangesAsync(cancellationToken);
                if (await context.CollectionFiles.AnyAsync(row => row.LeftAt == null && row.ValidatedAt == null, cancellationToken))
                    return RunResult.Handled(1);
                if (await context.VideoFiles.AnyAsync(file => !context.LibraryVerifications.Any(answer => answer.VideoFileId == file.Id), cancellationToken))
                    return RunResult.NothingToDo;
                preparation.Phase = "Importing";
                preparation.Error = null;
            }
            if (!await context.CollectionFiles.AnyAsync(file => file.LeftAt == null
                && !context.ArrivingFiles.Any(arrival => arrival.Id == file.Id && arrival.State == ArrivingFileState.Filed), cancellationToken))
            {
                preparation.Phase = "Ready";
                preparation.Error = null;
                preparation.Revision++;
            }
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Handled(1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            preparation.Error = "Collection import is waiting for readable sources and safe destinations. Restore access or retry the preview.";
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Failed(preparation.Error);
        }
    }
}

internal static class CollectionChanges
{
    public static bool SourceMatches(CollectionFileRow file, ArrivingFileRow arrival, VideoFileMover mover)
    {
        var info = new FileInfo(file.SourcePath);
        return info.Exists && info.Length == file.SizeBytes && info.LastWriteTimeUtc == file.LastWriteAt.UtcDateTime
            && mover.Matches(file.SourcePath, arrival.SizeBytes, arrival.OsHash);
    }

    public static async Task ReplanAsync(FabDbContext context, CollectionFileRow file, string detail, CancellationToken token)
    {
        file.ApprovedAt = null;
        file.ApprovedEvidence = null;
        file.PreviewEvidence = null;
        file.ValidatedAt = null;
        var preparation = await context.CollectionPreparations.AsTracking().SingleAsync(token);
        preparation.Phase = "Planning";
        preparation.Error = null;
        preparation.Revision++;
        file.PreviewDetail = detail;
        await context.SaveChangesAsync(token);
    }

    public static async Task RescanAsync(FabDbContext context, string detail, CancellationToken token)
    {
        var preparation = await context.CollectionPreparations.AsTracking().SingleAsync(token);
        preparation.Phase = "Inventory";
        preparation.Error = detail;
        preparation.Revision++;
        await context.CollectionWalks.ExecuteDeleteAsync(token);
        foreach (var file in await context.CollectionFiles.AsTracking().Where(file => file.LeftAt == null
            && !context.ArrivingFiles.Any(arrival => arrival.Id == file.Id && arrival.State == ArrivingFileState.Filed)).ToListAsync(token))
        {
            file.ApprovedAt = null;
            file.ApprovedEvidence = null;
            file.PreviewEvidence = null;
            file.ValidatedAt = null;
        }
        await context.SaveChangesAsync(token);
    }
}
