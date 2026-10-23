using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Prepares the preview in bounded background turns; GET reads only stored proposals.</summary>
public sealed class CollectionPlanningRoutine(FabDbContext context, FilingRoutine filing) : IRoutine
{
    public const string RoutineName = "Collection preview";
    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var preparation = await context.CollectionPreparations.AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (preparation?.Phase != "Planning") return RunResult.NothingToDo;
        var root = await context.Installation.Select(row => row.LibraryRoot).SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(root)) return RunResult.Failed("Configure the Library root before preparing an import.");
        var files = await context.CollectionFiles.AsTracking()
            .Where(row => row.LeftAt == null && row.PreviewEvidence == null
                && !context.ArrivingFiles.Any(arrival => arrival.Id == row.Id && arrival.State == ArrivingFileState.Filed))
            .OrderBy(row => row.Id).Take(25).ToListAsync(cancellationToken);
        foreach (var file in files)
        {
            var arrival = await context.ArrivingFiles.SingleOrDefaultAsync(row => row.Id == file.Id, cancellationToken);
            if (arrival is null)
            {
                preparation.Error = "A scanned file has no Identification record. Retry collection preparation.";
                await context.SaveChangesAsync(cancellationToken);
                return RunResult.Failed(preparation.Error);
            }
            file.PlannedPath = null;
            if (arrival.Reason is { } reason)
                file.PreviewDetail = reason.ToString();
            else
            {
                try
                {
                    var plan = await filing.PlanAsync(arrival, root, cancellationToken);
                    file.PlannedPath = plan.Path;
                    if (plan.Path != null && await context.CollectionFiles.AnyAsync(other => other.Id != file.Id
                        && other.LeftAt == null && other.PlannedPath == plan.Path, cancellationToken))
                    {
                        file.PlannedPath = null;
                        plan = new(null, null, "Another scanned file has this destination. Select one copy; leave or resolve the other.");
                    }
                    file.PreviewDetail = plan.Detail;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    file.PreviewDetail = "The destination could not be prepared safely. Resolve the Library path or metadata and retry preparation.";
                }
            }
            file.PreviewEvidence = CollectionEvidence.Of(file, arrival);
        }
        preparation.Revision++;
        await context.SaveChangesAsync(cancellationToken);
        if (!await context.CollectionFiles.AnyAsync(row => row.LeftAt == null && row.PreviewEvidence == null
                && !context.ArrivingFiles.Any(arrival => arrival.Id == row.Id && arrival.State == ArrivingFileState.Filed), cancellationToken))
        {
            preparation.Phase = "Preview";
            await context.SaveChangesAsync(cancellationToken);
        }
        return RunResult.Handled(Math.Max(1, files.Count));
    }
}
