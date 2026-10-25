using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Core.Acquisition;

/// <summary>Why ADR 0008 left a Release outside a Video's usable ranking.</summary>
public enum ReleaseExclusion
{
    PasswordProtected,
    ConfidenceNotAllowed,
    Consumed,
    MissingDownload,
}

public sealed record RankableRelease(
    long Id,
    Guid IndexerId,
    string DerivedReleaseId,
    long? Size,
    IdentificationConfidence? Confidence,
    int IndexerRank,
    bool PasswordProtected,
    bool Consumed,
    bool HasDownload);

public sealed record RankedRelease(RankableRelease Release, int Position);

public sealed record ExcludedRelease(RankableRelease Release, ReleaseExclusion Reason);

public sealed record ReleaseRankingResult(
    IReadOnlyList<RankedRelease> Ranked,
    IReadOnlyList<ExcludedRelease> Excluded);

/// <summary>ADR 0008's fixed, title-free and total ordering.</summary>
public static class ReleaseRanking
{
    public static bool HasUsableRelease(IEnumerable<RankableRelease> releases) =>
        releases.Any(release => ExclusionOf(release) is null);

    public static ReleaseRankingResult Order(IEnumerable<RankableRelease> releases)
    {
        var usable = new List<RankableRelease>();
        var excluded = new List<ExcludedRelease>();

        foreach (var release in releases)
        {
            var reason = ExclusionOf(release);
            if (reason is null) usable.Add(release);
            else excluded.Add(new ExcludedRelease(release, reason.Value));
        }

        var ordered = new List<RankableRelease>();
        foreach (var tier in usable.GroupBy(TierOf).OrderBy(group => group.Key))
        {
            var bySize = tier
                .OrderByDescending(release => release.Size.HasValue)
                .ThenByDescending(release => release.Size)
                .ThenBy(release => release.IndexerRank)
                .ThenBy(release => release.IndexerId)
                .ThenBy(release => release.DerivedReleaseId, StringComparer.Ordinal)
                .ToList();

            // Size cohorts are contiguous in this order. Walk each once instead
            // of scanning and removing from the entire remaining population.
            for (var start = 0; start < bySize.Count;)
            {
                var anchor = bySize[start].Size;
                var end = start + 1;
                while (end < bySize.Count && InCohort(bySize[end].Size, anchor)) end++;

                ordered.AddRange(bySize.GetRange(start, end - start)
                    .OrderBy(release => release.IndexerRank)
                    .ThenBy(release => release.IndexerId)
                    .ThenBy(release => release.DerivedReleaseId, StringComparer.Ordinal));

                start = end;
            }
        }

        return new ReleaseRankingResult(
            [.. ordered.Select((release, index) => new RankedRelease(release, index + 1))],
            [.. excluded
                .OrderBy(item => item.Reason)
                .ThenBy(item => item.Release.IndexerId)
                .ThenBy(item => item.Release.DerivedReleaseId, StringComparer.Ordinal)]);
    }

    private static ReleaseExclusion? ExclusionOf(RankableRelease release)
    {
        if (release.PasswordProtected) return ReleaseExclusion.PasswordProtected;
        if (TierOf(release) is null) return ReleaseExclusion.ConfidenceNotAllowed;
        if (release.Consumed) return ReleaseExclusion.Consumed;
        if (!release.HasDownload) return ReleaseExclusion.MissingDownload;
        return null;
    }

    private static int? TierOf(RankableRelease release) => release.Confidence switch
    {
        IdentificationConfidence.Exact or IdentificationConfidence.Strong => 0,
        IdentificationConfidence.Probable => 1,
        _ => null,
    };

    private static bool InCohort(long? size, long? anchor)
    {
        if (anchor is null) return size is null;
        if (size is null) return false;
        // A reported zero size must still form a cohort and make progress.
        if (anchor <= 0) return size == anchor;

        // Written without floating point: the difference is strictly below
        // five percent of the larger (the cohort anchor).
        return (decimal)(anchor.Value - size.Value) * 100m < (decimal)anchor.Value * 5m;
    }
}
