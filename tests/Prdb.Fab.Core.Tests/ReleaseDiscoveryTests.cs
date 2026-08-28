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
        Assert.Equal("a release title 2026", ReleaseTitle.Normalise(" A.Release_Title-2026.mkv "));
    }

    [Fact]
    public void The_identification_state_set_is_closed_in_one_place()
    {
        Assert.Equal(
            ["Unexamined", "Unremarkable", "Awaiting", "Matched", "SiteOnly", "Ambiguous", "Unknown"],
            Enum.GetNames<IdentificationState>());
    }
}
