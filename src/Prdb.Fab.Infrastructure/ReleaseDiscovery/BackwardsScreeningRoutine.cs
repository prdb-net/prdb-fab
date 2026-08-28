using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Screening in the backwards direction, when the Catalogue learns a new needle.</summary>
public sealed class BackwardsScreeningRoutine(
    FabDbContext context,
    CataloguePins pins,
    ILogger<BackwardsScreeningRoutine> logger) : IRoutine
{
    private static readonly IdentificationState[] Reconsidered =
        [IdentificationState.Unremarkable, IdentificationState.SiteOnly, IdentificationState.Unknown];

    public string Name => DiscoveryRoutineNames.BackwardsSearch;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var pinnedIds = await pins.Pinned(context.CatalogueVideos)
            .Select(video => video.Id)
            .ToListAsync(cancellationToken);

        var titles = await context.CatalogueVideos
            .AsTracking()
            .Where(video => pinnedIds.Contains(video.Id) && !video.TitleSearchedBackwards)
            .ToListAsync(cancellationToken);
        var preNames = await context.CatalogueVideoPreNames
            .AsTracking()
            .Where(preName => pinnedIds.Contains(preName.VideoId) && !preName.SearchedBackwards)
            .ToListAsync(cancellationToken);

        if (titles.Count == 0 && preNames.Count == 0)
        {
            return RunResult.NothingToDo;
        }

        var needles = titles
            .Select(video => video.NormalisedTitle)
            .Concat(preNames.Select(preName => preName.NormalisedPreName))
            .Where(needle => needle.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // One indexless pass over the bounded cache for the whole accumulated
        // needle batch. Never one LIKE query per needle, and no FTS table.
        var releases = await context.Releases
            .AsTracking()
            .Where(release => Reconsidered.Contains(release.IdentificationState))
            .ToListAsync(cancellationToken);
        var hits = 0;

        foreach (var release in releases)
        {
            if (!Screening.Hits(release.NormalisedTitle, needles))
            {
                continue;
            }

            release.IdentificationState = IdentificationState.Awaiting;
            release.VideoId = null;
            release.Confidence = null;
            release.MatchedBy = null;
            release.SiteId = null;
            release.SearchWasReason = false;
            hits++;
        }

        foreach (var title in titles)
        {
            title.TitleSearchedBackwards = true;
        }

        foreach (var preName in preNames)
        {
            preName.SearchedBackwards = true;
        }

        // The needle flags and every hit commit together. A crash before this
        // commit repeats the idempotent pass; it cannot strand a new needle.
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Backwards Screening searched {Needles} Catalogue needle(s) and reconsidered {Hits} Release(s).",
            titles.Count + preNames.Count,
            hits);

        return RunResult.Handled(titles.Count + preNames.Count);
    }
}
