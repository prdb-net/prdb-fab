using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Screening in the forward direction, over new cached Releases.</summary>
public sealed class ScreeningRoutine(
    FabDbContext context,
    CataloguePins pins,
    ILogger<ScreeningRoutine> logger) : IRoutine
{
    public const int BatchSize = 1000;

    public string Name => DiscoveryRoutineNames.Screening;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var releases = await context.Releases
            .AsTracking()
            .Where(row => row.IdentificationState == IdentificationState.Unexamined)
            .OrderBy(row => row.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (releases.Count == 0)
        {
            return RunResult.NothingToDo;
        }

        var catalogue = await pins.Pinned(context.CatalogueVideos)
            .Select(video => new { video.Id, video.NormalisedTitle, video.LastReadAt })
            .ToListAsync(cancellationToken);
        var videoIds = catalogue.Select(video => video.Id).ToArray();
        var preNames = await context.CatalogueVideoPreNames
            .Where(row => videoIds.Contains(row.VideoId))
            .Select(row => row.NormalisedPreName)
            .ToListAsync(cancellationToken);
        var needles = catalogue
            .Select(video => video.NormalisedTitle)
            .Concat(preNames)
            .Where(needle => needle.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // A feed summary creates a pinned catalogue row before its detail read
        // brings the authoritative Pre-Names. Until every pinned row has had
        // that read, absence is not a final miss.
        var catalogueReady = catalogue.All(video => video.LastReadAt != default);
        var handled = 0;

        foreach (var release in releases)
        {
            if (Screening.Hits(release.NormalisedTitle, needles))
            {
                release.IdentificationState = IdentificationState.Awaiting;
                handled++;
            }
            else if (catalogueReady)
            {
                release.IdentificationState = IdentificationState.Unremarkable;
                handled++;
            }
        }

        if (handled == 0)
        {
            return RunResult.NothingToDo;
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Screening examined {Count} Release(s); {Awaiting} now await prdb Identification.",
            handled,
            releases.Count(row => row.IdentificationState == IdentificationState.Awaiting));

        return RunResult.Handled(handled);
    }
}
