using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.ReleaseDiscovery;

using Xunit;

namespace Prdb.Fab.Core.Tests;

public sealed class ReleaseDiscoveryTests
{
    [Fact]
    public void The_attribute_guid_wins_over_every_other_shape()
    {
        Assert.Equal("attribute-id", ReleaseIdentity.From(" attribute-id ", "https://indexer.invalid/details/uri-id"));
    }

    [Fact]
    public void A_uri_guid_uses_its_last_path_segment()
    {
        Assert.Equal("stable-id", ReleaseIdentity.From(null, "https://indexer.invalid/details/stable-id"));
    }

    [Fact]
    public void A_spotweb_message_id_is_already_an_identity()
    {
        Assert.Equal("<post.42@example.invalid>", ReleaseIdentity.From(null, "<post.42@example.invalid>"));
    }

    [Fact]
    public void A_title_has_one_punctuation_and_extension_independent_form()
    {
        Assert.Equal("a release title 2026", ComparisonForm.Of(" A.Release_Title-2026.mkv "));
    }

    [Fact]
    public void The_identification_state_set_is_closed_in_one_place()
    {
        Assert.Equal(
            ["Unexamined", "Unremarkable", "Awaiting", "Matched", "SiteOnly", "Ambiguous", "Unknown"],
            Enum.GetNames<IdentificationState>());
    }

    [Theory]
    [InlineData(1000, 480)]
    [InlineData(100, 50)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void The_wanted_sweep_reserves_half_the_daily_budget_up_to_its_cadence(
        int dailyBudget,
        int reserved)
    {
        Assert.Equal(reserved, IndexerQueryBudget.ReservedForSweep(dailyBudget));
    }

    [Fact]
    public void A_walk_cannot_spend_the_wanted_sweeps_reservation()
    {
        Assert.True(IndexerQueryBudget.Admits(10, spent: 4, spentBySweep: 0, purpose: IndexerQueryPurpose.Walk));
        Assert.False(IndexerQueryBudget.Admits(10, spent: 5, spentBySweep: 0, purpose: IndexerQueryPurpose.Walk));
        Assert.True(IndexerQueryBudget.Admits(10, spent: 5, spentBySweep: 0, purpose: IndexerQueryPurpose.WantedSweep));
        Assert.True(IndexerQueryBudget.Admits(
            10,
            spent: 5,
            spentBySweep: 0,
            purpose: IndexerQueryPurpose.Walk,
            sweepHasWork: false));
    }

    [Fact]
    public void A_manual_search_can_preempt_a_walk_but_not_the_wanted_sweeps_reservation()
    {
        Assert.True(IndexerQueryBudget.Admits(
            10, spent: 4, spentBySweep: 0, purpose: IndexerQueryPurpose.ManualSearch));
        Assert.False(IndexerQueryBudget.Admits(
            10, spent: 5, spentBySweep: 0, purpose: IndexerQueryPurpose.ManualSearch));
    }

    [Theory]
    [InlineData("A Long Title", "A Long Title", true)]
    [InlineData("Scene 3", "Scene 3", false)]
    [InlineData("One", "One", false)]
    public void A_wanted_search_title_is_for_the_indexers_tokeniser(
        string title,
        string query,
        bool searchable)
    {
        Assert.Equal(query, WantedSearchTitle.Of(title));
        Assert.Equal(searchable, WantedSearchTitle.IsSearchable(query));
    }
}
