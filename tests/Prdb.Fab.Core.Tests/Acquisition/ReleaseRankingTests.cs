using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.ReleaseDiscovery;

using Xunit;

namespace Prdb.Fab.Core.Tests.Acquisition;

public sealed class ReleaseRankingTests
{
    [Fact]
    public void Confidence_size_cohorts_indexer_rank_and_identity_form_one_total_order()
    {
        var state = new[]
        {
            Release(1, "large", 1000, IdentificationConfidence.Strong, rank: 4),
            Release(2, "near-b", 960, IdentificationConfidence.Exact, rank: 2),
            Release(3, "near-a", 960, IdentificationConfidence.Exact, rank: 1),
            Release(4, "next", 949, IdentificationConfidence.Exact, rank: 0),
            Release(5, "probable", 5000, IdentificationConfidence.Probable, rank: 0),
        };

        var first = ReleaseRanking.Order(state).Ranked.Select(item => item.Release.DerivedReleaseId).ToArray();
        var reversed = ReleaseRanking.Order(state.Reverse()).Ranked.Select(item => item.Release.DerivedReleaseId).ToArray();

        Assert.Equal(["near-a", "near-b", "large", "next", "probable"], first);
        Assert.Equal(first, reversed);
    }

    [Fact]
    public void Exclusions_are_named_and_titles_are_not_an_input()
    {
        var result = ReleaseRanking.Order(
        [
            Release(1, "password", 100, IdentificationConfidence.Exact, password: true),
            Release(2, "confidence", 100, IdentificationConfidence.Partial),
            Release(3, "consumed", 100, IdentificationConfidence.Exact, consumed: true),
            Release(4, "no-download", 100, IdentificationConfidence.Exact, hasDownload: false),
        ]);

        Assert.Empty(result.Ranked);
        Assert.Equal(
            [
                ReleaseExclusion.PasswordProtected,
                ReleaseExclusion.ConfidenceNotAllowed,
                ReleaseExclusion.Consumed,
                ReleaseExclusion.MissingDownload,
            ],
            result.Excluded.OrderBy(item => item.Release.Id).Select(item => item.Reason));
    }

    [Fact]
    public void Known_sizes_precede_the_missing_size_cohort()
    {
        var result = ReleaseRanking.Order(
        [
            Release(1, "unknown-b", null, IdentificationConfidence.Exact, rank: 2),
            Release(2, "known", 1, IdentificationConfidence.Exact, rank: 9),
            Release(3, "unknown-a", null, IdentificationConfidence.Exact, rank: 1),
        ]);

        Assert.Equal(
            ["known", "unknown-a", "unknown-b"],
            result.Ranked.Select(item => item.Release.DerivedReleaseId));
    }

    private static RankableRelease Release(
        long id,
        string identity,
        long? size,
        IdentificationConfidence confidence,
        int rank = 0,
        bool password = false,
        bool consumed = false,
        bool hasDownload = true) => new(
            id,
            Guid.Parse($"00000000-0000-4000-8000-{id:000000000000}"),
            identity,
            size,
            confidence,
            rank,
            password,
            consumed,
            hasDownload);
}
