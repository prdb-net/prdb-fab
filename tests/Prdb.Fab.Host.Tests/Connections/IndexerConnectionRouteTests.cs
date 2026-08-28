using System.Net;
using System.Net.Http.Json;
using System.Text;

using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// ADR 0010's skippable search step, end to end: a real search rather than a
/// capabilities call, and a category tree matched by name rather than by number.
/// </summary>
public sealed class IndexerConnectionRouteTests
{
    private const string One = "http://one.invalid/api";

    private const string Two = "http://two.invalid/api";

    /// <summary>
    /// The finding ADR 0010 built the rule on: three of the four implementations
    /// surveyed answer <c>t=caps</c> without a key at all. This fake does the
    /// same, so a check that leaned on capabilities would confirm any key here —
    /// and the search is what actually asks.
    /// </summary>
    [Fact]
    public async Task A_wrong_key_is_caught_by_the_search_though_caps_answers_anybody()
    {
        var indexer = new FakeIndexer();
        await using var application = Answering(indexer);
        var client = await application.SignedInClientAsync();

        var verdict = await AddAsync(client, One, "not the key");

        Assert.Equal("WrongKey", verdict.Outcome);
        Assert.Equal("search", indexer.Functions[0]);
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task An_indexer_that_answers_a_real_search_is_added()
    {
        var indexer = new FakeIndexer();
        await using var application = Answering(indexer);
        var client = await application.SignedInClientAsync();

        var verdict = await AddAsync(client, One, FakeIndexer.RightKey);

        Assert.Equal("Saved", verdict.Outcome);
        Assert.Equal(["XXX", "XXX/DVD", "XXX/x264", "XXX/UHD", "XXX/WEB-DL"], verdict.Categories);

        var added = Assert.Single(await ListAsync(client));

        Assert.Equal(One, added.Url);
        Assert.Equal("one.invalid", added.Name);

        // CONTEXT.md puts the verdict of the last check on a Connection, and
        // ADR 0033 reserves the word for exactly this. Nothing displays it yet;
        // the Status page is a slice of its own.
        Assert.Equal("Saved", added.LastVerdict);
    }

    /// <summary>
    /// The whole reason for matching by name. Two capabilities documents
    /// describing the same tree at different numbers have to be stored
    /// identically — <c>6070</c> means <em>Packs</em> in the spec and in one
    /// server, and <em>Other</em> in a widely used client's canonical table.
    /// </summary>
    [Fact]
    public async Task The_same_tree_numbered_differently_is_stored_the_same()
    {
        var spec = await AddedCategoriesAsync("caps.xml");
        var renumbered = await AddedCategoriesAsync("caps-renumbered.xml");

        Assert.Equal(spec, renumbered);
        Assert.NotEmpty(spec);
    }

    [Fact]
    public async Task A_second_indexer_is_a_second_row()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        Assert.Equal("Saved", (await AddAsync(client, One, FakeIndexer.RightKey)).Outcome);
        Assert.Equal("Saved", (await AddAsync(client, Two, FakeIndexer.RightKey)).Outcome);

        var indexers = await ListAsync(client);

        Assert.Equal(2, indexers.Count);
        Assert.Distinct(indexers.Select(indexer => indexer.Id));
        Assert.Equal([0, 1], indexers.Select(indexer => indexer.Rank).Order());
    }

    /// <summary>
    /// ADR 0002 identifies a release by the indexer together with that indexer's
    /// own id for it, so two rows for one address would give one package two
    /// identities — and spend ADR 0024's budget twice on the way there.
    /// </summary>
    [Fact]
    public async Task The_same_indexer_twice_is_refused()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        await AddAsync(client, One, FakeIndexer.RightKey);

        Assert.Equal("AlreadyAdded", (await AddAsync(client, One, FakeIndexer.RightKey)).Outcome);
        Assert.Single(await ListAsync(client));
    }

    /// <summary>
    /// The step is skippable, and skipping it adds none. Ticket 09 builds the
    /// act; this is what it leaves behind.
    /// </summary>
    [Fact]
    public async Task A_step_nobody_took_adds_none()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task An_indexer_with_nothing_this_tool_searches_is_not_added()
    {
        await using var application = Answering(new FakeIndexer { Caps = "caps-nothing-here.xml" });
        var client = await application.SignedInClientAsync();

        Assert.Equal("NoCategories", (await AddAsync(client, One, FakeIndexer.RightKey)).Outcome);
        Assert.Empty(await ListAsync(client));
    }

    /// <summary>
    /// A page rather than a feed is what a blocked address, a login wall or a
    /// proxy answering for something that is not there all look like.
    /// </summary>
    [Fact]
    public async Task A_page_instead_of_a_feed_says_so()
    {
        await using var application = Answering(new NotAnIndexer());
        var client = await application.SignedInClientAsync();

        Assert.Equal("NotAnIndexer", (await AddAsync(client, One, FakeIndexer.RightKey)).Outcome);
    }

    /// <summary>
    /// A spent budget is not a wrong key, and the two send the user to entirely
    /// different places.
    /// </summary>
    [Fact]
    public async Task A_spent_budget_is_its_own_verdict()
    {
        await using var application = Answering(new FakeIndexer
        {
            SearchStatus = HttpStatusCode.TooManyRequests,
            SearchBody = """<?xml version="1.0"?><error code="500" description="Request limit reached"/>""",
        });

        var client = await application.SignedInClientAsync();

        Assert.Equal("LimitReached", (await AddAsync(client, One, FakeIndexer.RightKey)).Outcome);
    }

    /// <summary>
    /// Carried back in the indexer's own words, because the five
    /// implementations surveyed agree on nothing except the shape of the
    /// document that says so.
    /// </summary>
    [Fact]
    public async Task Any_other_refusal_is_repeated_in_the_indexers_own_words()
    {
        await using var application = Answering(new FakeIndexer
        {
            SearchBody = """<?xml version="1.0"?><error code="900" description="Something specific"/>""",
        });

        var client = await application.SignedInClientAsync();
        var verdict = await AddAsync(client, One, FakeIndexer.RightKey);

        Assert.Equal("Refused", verdict.Outcome);
        Assert.Contains("Something specific", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_answering_is_not_a_wrong_key()
    {
        await using var application = Answering(new FakeIndexer
        {
            Throws = new HttpRequestException("Connection refused"),
        });

        var client = await application.SignedInClientAsync();

        Assert.Equal("NotRightNow", (await AddAsync(client, One, FakeIndexer.RightKey)).Outcome);
    }

    private static FabApplication Answering(HttpMessageHandler indexer) =>
        new FabApplication().Answering(FabTransports.Indexers, indexer);

    private static async Task<IReadOnlyList<string>> AddedCategoriesAsync(string caps)
    {
        await using var application = Answering(new FakeIndexer { Caps = caps });
        var client = await application.SignedInClientAsync();

        var added = await AddAsync(client, One, FakeIndexer.RightKey);

        Assert.Equal("Saved", added.Outcome);

        return added.Categories;
    }

    private static async Task<Verdict> AddAsync(HttpClient client, string url, string apiKey)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/indexers",
            new { name = (string?)null, url, apiKey },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<IReadOnlyList<Configured>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Configured>>(
            "/api/connections/indexers",
            TestContext.Current.CancellationToken))!;

    /// <summary>Something else answering at that address, with a page.</summary>
    private sealed class NotAnIndexer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><body>Please sign in.</body></html>",
                    Encoding.UTF8,
                    "text/html"),
            });
    }

    private sealed record Verdict(string Outcome, string Detail, IReadOnlyList<string> Categories);

    private sealed record Configured(
        Guid Id,
        string Name,
        string Url,
        string Categories,
        int Rank,
        string LastVerdict,
        DateTimeOffset LastCheckedAt);
}
