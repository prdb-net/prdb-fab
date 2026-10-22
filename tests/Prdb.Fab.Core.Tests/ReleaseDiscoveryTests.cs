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

    [Fact]
    public void Historical_work_preserves_measured_head_and_minimum_capacity()
    {
        var plan = IndexerQueryBudget.Plan(1000, 0, new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero),
            measuredHeadPages: 1, minimumRemaining: 373, searchableWanted: 1, manualRemaining: 2);
        Assert.Equal(96, plan.HeadReserve);
        Assert.Equal(373, plan.MinimumReserve);
        Assert.Equal(98, plan.SearchReserve);
        Assert.Equal(433, plan.AvailableFor(IndexerQueryPurpose.OptionalHistory));
        Assert.Equal(531, plan.AvailableFor(IndexerQueryPurpose.ManualSearch));
        Assert.Equal(904, plan.AvailableFor(IndexerQueryPurpose.MinimumWindow));
    }

    [Fact]
    public void A_small_plan_reports_the_shortfall_and_still_allows_head_polling()
    {
        var plan = new IndexerBudgetPlan(10, 0, 96, 373, 0);
        Assert.Equal(459, plan.RequiredShortfall);
        Assert.Equal(0, plan.AvailableFor(IndexerQueryPurpose.OptionalHistory));
        Assert.Equal(0, plan.AvailableFor(IndexerQueryPurpose.ManualSearch));
        Assert.Equal(10, plan.AvailableFor(IndexerQueryPurpose.Walk));
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
