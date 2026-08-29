using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Turns completed SABnzbd storage into durable, already-probed arrivals.</summary>
public sealed class CollectingRoutine(
    FabDbContext context,
    VideoProbe probe,
    TimeProvider time,
    ILogger<CollectingRoutine> logger) : IRoutine
{
    public const string RoutineName = "Collecting";

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromMinutes(1);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var downloads = await context.Downloads
            .AsTracking()
            .Where(row => row.State == DownloadState.Completed)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        if (downloads.Count == 0)
        {
            return RunResult.NothingToDo;
        }

        var mapping = await context.Installation.AsNoTracking().Select(row => new
        {
            row.PathMappingFrom,
            row.PathMappingTo,
        }).SingleAsync(cancellationToken);

        var handled = 0;
        foreach (var download in downloads)
        {
            var root = PathMapping.Resolve(mapping.PathMappingFrom, mapping.PathMappingTo, download.Storage);
            if (root is null)
            {
                logger.LogWarning(
                    "A completed Download path does not fall under the configured Path Mapping.");
                continue;
            }

            IReadOnlyList<string> paths;
            try
            {
                paths = SupportedFiles(root);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogReadableNeighbour(root);
                continue;
            }

            var already = await context.ArrivingFiles
                .Where(row => row.DownloadId == download.Id)
                .Select(row => row.SourcePath)
                .ToListAsync(cancellationToken);
            var held = already.ToHashSet(StringComparer.Ordinal);

            foreach (var path in paths.Where(path => !held.Contains(path)))
            {
                var reading = await probe.ReadAsync(path, cancellationToken);
                if (reading.Outcome == ProbeOutcome.SourceMissing)
                {
                    logger.LogWarning(
                        "A supported Video File disappeared while its completed Download was being collected.");
                }

                var reason = await LocalReasonAsync(reading, cancellationToken);
                context.ArrivingFiles.Add(new ArrivingFileRow
                {
                    Id = Guid.CreateVersion7(time.GetUtcNow()),
                    DownloadId = download.Id,
                    IndexerId = download.IndexerId,
                    DerivedReleaseId = download.DerivedReleaseId,
                    SourcePath = path,
                    ArrivedName = Path.GetFileName(path),
                    IsOnDisk = reading.Outcome != ProbeOutcome.SourceMissing,
                    State = ArrivingFileState.AwaitingIdentification,
                    Reason = reason,
                    SizeBytes = reading.SizeBytes,
                    RuntimeSeconds = reading.RuntimeSeconds,
                    Width = reading.Width,
                    Height = reading.Height,
                    VideoCodec = reading.VideoCodec,
                    QualityLabel = reading.QualityLabel,
                    OsHash = reading.OsHash,
                    ProbeOutcome = reading.Outcome,
                    ProbeError = reading.Error,
                });

                // The inserted row is the evidence that this path has already
                // been probed. Committing one at a time makes restart resume at
                // the next file rather than opening this one again.
                await context.SaveChangesAsync(cancellationToken);
                held.Add(path);
            }

            if (held.Count == 0)
            {
                download.State = DownloadState.Failed;
                download.Cause = DownloadCause.Empty;
            }
            else
            {
                download.State = DownloadState.Collected;
            }

            await context.SaveChangesAsync(cancellationToken);
            if (download.State == DownloadState.Failed && !download.OriginIsPerson)
            {
                var localVideoId = await context.CatalogueVideos
                    .Where(row => row.PrdbId == download.VideoId)
                    .Select(row => (long?)row.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (localVideoId is not null
                    && await context.WantedVideos.AnyAsync(
                        row => row.VideoId == localVideoId,
                        cancellationToken))
                {
                    await context.Releases
                        .Where(row => row.VideoId == localVideoId
                            && row.IdentificationState == Prdb.Fab.Core.ReleaseDiscovery.IdentificationState.Matched)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(row => row.AutomationPending, true)
                            .SetProperty(row => row.AutomationDecisionReason, (AutomationDecisionReason?)null),
                            cancellationToken);
                }
            }
            handled++;
        }

        // A path Gap deliberately is not a failed run: failure would apply the
        // scheduler's backoff, while ADR 0016 requires a fixed one-minute retry
        // until the Path Mapping is repaired.
        return RunResult.Handled(handled);
    }

    private async Task<ArrivingFileReason?> LocalReasonAsync(
        VideoProbeReading reading,
        CancellationToken cancellationToken)
    {
        if (reading.OsHash is { } hash
            && await context.VideoFiles.AnyAsync(row => row.OsHash == hash, cancellationToken))
        {
            return ArrivingFileReason.IdenticalFile;
        }

        return reading.QualityLabel is null ? ArrivingFileReason.UnreadableQuality : null;
    }

    private static IReadOnlyList<string> SupportedFiles(string root)
    {
        if (File.Exists(root))
        {
            return VideoFiles.IsSupported(Path.GetFileName(root)) ? [Path.GetFullPath(root)] : [];
        }

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException();
        }

        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (VideoFiles.IsSupported(Path.GetFileName(file)))
                {
                    found.Add(Path.GetFullPath(file));
                }
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

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private void LogReadableNeighbour(string path)
    {
        var candidate = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (candidate is not null)
        {
            try
            {
                var names = Directory.EnumerateFileSystemEntries(candidate)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null)
                    .Take(25)
                    .ToArray();
                logger.LogWarning(
                    "Collecting could read the ancestor directory {DirectoryName}; it contained {Names}.",
                    Path.GetFileName(candidate),
                    names);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                candidate = Path.GetDirectoryName(candidate);
            }
        }

        logger.LogWarning("Collecting could not read the completed Download path or any ancestor directory.");
    }
}
