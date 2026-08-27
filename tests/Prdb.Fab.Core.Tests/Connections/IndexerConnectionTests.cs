using Prdb.Fab.Core.Connections;

using Xunit;

namespace Prdb.Fab.Core.Tests.Connections;

/// <summary>
/// What an indexer's refusal means, and which of its categories this tool
/// searches. Both are rules rather than parsing, and both exist because the
/// research found five implementations disagreeing.
/// </summary>
public sealed class IndexerConnectionTests
{
    /// <summary>
    /// The four shapes a wrong key arrives in, one per implementation surveyed.
    /// The spec says code 100 at HTTP 200; one server sends code 403 at HTTP
    /// 403, another code 100 at HTTP 401. Reading any single one of those
    /// signals would miss two of the others.
    /// </summary>
    [Theory]
    [InlineData(200, 100, "Incorrect user credentials")]
    [InlineData(403, 403, "Incorrect user credentials (wrong API key)")]
    [InlineData(401, 100, "Incorrect user credentials (wrong API key)")]
    [InlineData(200, 200, "Missing parameter (apikey)")]
    public void A_wrong_key_is_recognised_however_it_arrives(int status, int code, string description) =>
        Assert.Equal(
            IndexerConnectionOutcome.WrongKey,
            IndexerConnection.ForError(status, code, description));

    /// <summary>
    /// The spec has 500 and 501 for this; one server uses 429 as an error code
    /// as well as a status; newznab classic has neither and only the words.
    /// </summary>
    [Theory]
    [InlineData(200, 500, "Request limit reached")]
    [InlineData(429, 429, "Request limit reached")]
    [InlineData(200, 900, "Request limit reached")]
    public void A_spent_budget_is_not_a_wrong_key(int status, int code, string description) =>
        Assert.Equal(
            IndexerConnectionOutcome.LimitReached,
            IndexerConnection.ForError(status, code, description));

    /// <summary>
    /// Anything else is carried back in the indexer's own words rather than
    /// classified into a bucket this project invented.
    /// </summary>
    [Fact]
    public void Anything_else_is_the_indexers_own_refusal() =>
        Assert.Equal(
            IndexerConnectionOutcome.Refused,
            IndexerConnection.ForError(200, 202, "No such function"));

    /// <summary>
    /// The point of matching by name. Two capabilities documents describing the
    /// same tree with different numbers have to produce the same answer, because
    /// the numbers are the indexer's own and the research found them
    /// contradicting each other across implementations.
    /// </summary>
    [Fact]
    public void The_same_tree_numbered_differently_matches_the_same()
    {
        CapsCategory[] spec =
        [
            new(2000, "Movies", [new CapsCategory(2040, "HD")]),
            new(6000, "XXX", [new CapsCategory(6010, "DVD"), new CapsCategory(6040, "x264")]),
        ];

        CapsCategory[] renumbered =
        [
            new(2000, "Movies", [new CapsCategory(2045, "HD")]),
            new(7000, "XXX", [new CapsCategory(7010, "DVD"), new CapsCategory(7050, "x264")]),
        ];

        Assert.Equal(IndexerConnection.MatchedByName(spec), IndexerConnection.MatchedByName(renumbered));
    }

    [Fact]
    public void A_child_is_qualified_by_its_parent() =>
        Assert.Equal(
            ["XXX", "XXX/DVD", "XXX/x264"],
            IndexerConnection.MatchedByName(
            [
                new CapsCategory(6000, "XXX", [new CapsCategory(6010, "DVD"), new CapsCategory(6040, "x264")]),
            ]));

    [Fact]
    public void An_indexer_with_nothing_this_tool_searches_matches_nothing() =>
        Assert.Empty(IndexerConnection.MatchedByName(
        [
            new CapsCategory(5000, "TV", [new CapsCategory(5040, "HD")]),
        ]));

    /// <summary>
    /// Both spellings that appear in the wild. The tree is the indexer's own,
    /// and nobody agreed on one word for it.
    /// </summary>
    [Theory]
    [InlineData("XXX")]
    [InlineData("xxx")]
    [InlineData("Adult")]
    public void The_names_that_count_are_matched_however_they_are_spelt(string name) =>
        Assert.Equal(
            [name],
            IndexerConnection.MatchedByName([new CapsCategory(6000, name)]));

    [Fact]
    public void Every_verdict_says_something()
    {
        foreach (var outcome in Enum.GetValues<IndexerConnectionOutcome>())
        {
            Assert.NotEmpty(IndexerConnection.Sentence(outcome, "because it felt like it"));
        }
    }
}
