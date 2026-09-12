using System.Net;
using System.Net.Http.Json;

using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Host.Tests.Connections;

/// <summary>
/// ADR 0020's three row settings, the order that is the rank, and what deleting
/// an Indexer costs — the half of the Indexer route that had no control at all
/// until ADR 0058.
/// </summary>
public sealed class IndexerRowRouteTests
{
    private const string One = "http://one.invalid/api";

    private const string Two = "http://two.invalid/api";

    /// <summary>
    /// The reason the settings are an act of their own: changing them must not
    /// spend a query at the indexer, or disabling a broken one would be
    /// impossible — the check would fail and take the change with it.
    /// </summary>
    [Fact]
    public async Task Enabling_and_the_budget_are_stored_without_a_second_search()
    {
        var indexer = new FakeIndexer();
        await using var application = Answering(indexer);
        var client = await application.SignedInClientAsync();
        var added = await AddAsync(client, One);

        // The indexer goes dark, which is the case this is actually about:
        // disabling a broken one has to be possible, and an act that re-checked
        // would fail the check and take the change with it.
        //
        // Asserting instead that the act made no call at all would be asserting
        // about a transport the background lanes share — they walk this same
        // fake, and in a full suite run they get the time to do it.
        indexer.Throws = new HttpRequestException("the indexer is not answering");

        var verdict = await SettingsAsync(client, added.Id, enabled: false, dailyQueryBudget: 250);

        Assert.True(verdict.Saved);
        Assert.False(verdict.Enabled);
        Assert.Equal(250, verdict.DailyQueryBudget);

        var stored = Assert.Single(await ListAsync(client));

        Assert.False(stored.Enabled);
        Assert.Equal(250, stored.DailyQueryBudget);
    }

    /// <summary>
    /// The Status Brake *"…'s daily query budget is spent"* points at this
    /// route, and the number it is about now travels with the row.
    /// </summary>
    [Fact]
    public async Task The_budget_the_brake_is_about_is_on_the_row()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        await AddAsync(client, One);

        var stored = Assert.Single(await ListAsync(client));

        Assert.Equal(1000, stored.DailyQueryBudget);
    }

    /// <summary>Empty is ADR 0020's unbounded, and it says which one it holds.</summary>
    [Fact]
    public async Task An_empty_budget_is_unbounded()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();
        var added = await AddAsync(client, One);

        var verdict = await SettingsAsync(client, added.Id, enabled: true, dailyQueryBudget: null);

        Assert.True(verdict.Saved);
        Assert.Equal(Indexers.Unbounded, verdict.DailyQueryBudget);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public async Task A_budget_no_day_can_hold_is_refused_and_changes_nothing(int budget)
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();
        var added = await AddAsync(client, One);

        var verdict = await SettingsAsync(client, added.Id, enabled: true, dailyQueryBudget: budget);

        Assert.False(verdict.Saved);
        Assert.Equal(1000, Assert.Single(await ListAsync(client)).DailyQueryBudget);
    }

    /// <summary>
    /// ADR 0020 made the rank a list position, so the list is what the order is
    /// read from and a move is what changes it.
    /// </summary>
    [Fact]
    public async Task The_rank_is_the_list_position_and_a_move_is_a_save()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        var first = await AddAsync(client, One);
        var second = await AddAsync(client, Two);

        Assert.Equal([first.Id, second.Id], (await ListAsync(client)).Select(row => row.Id));

        using (var moved = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{second.Id}/move",
            new { up = true },
            TestContext.Current.CancellationToken))
        {
            moved.EnsureSuccessStatusCode();
        }

        Assert.Equal([second.Id, first.Id], (await ListAsync(client)).Select(row => row.Id));

        // Renumbered from zero rather than two rows swapping numbers, so the
        // order ADR 0008 needs stays total however the ranks arrived.
        Assert.Equal([0, 1], (await ListAsync(client)).Select(row => row.Rank));
    }

    [Fact]
    public async Task Moving_past_the_end_changes_nothing()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        var first = await AddAsync(client, One);
        var second = await AddAsync(client, Two);

        using var moved = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{first.Id}/move",
            new { up = true },
            TestContext.Current.CancellationToken);

        moved.EnsureSuccessStatusCode();

        Assert.Equal([first.Id, second.Id], (await ListAsync(client)).Select(row => row.Id));
    }

    /// <summary>
    /// The preview names the three different answers: the cache goes, the
    /// Downloads stay, and a rule that permits it loses that permission.
    /// </summary>
    [Fact]
    public async Task The_delete_preview_names_what_goes_and_what_stays()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();
        var added = await AddAsync(client, One);

        await ARuleAllowing(client, added.Id, "Only this one");

        var preview = await PreviewDeleteAsync(client, added.Id);

        Assert.Equal(added.Id, preview.IndexerId);
        Assert.Equal("one.invalid", preview.Name);
        Assert.Equal(0, preview.Downloads);
        Assert.Equal(1, preview.RulesReferencing);
        Assert.Equal(1, preview.RulesLosingTheirLastIndexer);
    }

    /// <summary>
    /// ADR 0020 is explicit that a rule left with no permitted Indexer comes
    /// back <em>disabled</em>. ADR 0018 is the reason: a disabled rule shows as
    /// a Brake, and an inert one is the silent failure that page exists to
    /// prevent.
    /// </summary>
    [Fact]
    public async Task A_rule_left_with_no_indexer_comes_back_disabled()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();

        var doomed = await AddAsync(client, One);
        var surviving = await AddAsync(client, Two);

        var alone = await ARuleAllowing(client, doomed.Id, "Only the doomed one");
        var both = await ARuleAllowing(client, doomed.Id, "Both of them", surviving.Id);

        using (var response = await client.PostAsync(
            $"/api/connections/indexers/{doomed.Id}/delete",
            content: null,
            TestContext.Current.CancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var verdict = (await response.Content.ReadFromJsonAsync<DeleteVerdict>(
                TestContext.Current.CancellationToken))!;

            Assert.Equal(1, verdict.RulesDisabled);
        }

        Assert.Equal(surviving.Id, Assert.Single(await ListAsync(client)).Id);

        var rules = await RulesAsync(client);

        Assert.False(rules.Single(rule => rule.Id == alone).Enabled);

        // The other one still has an Indexer, so nothing about it changed.
        Assert.True(rules.Single(rule => rule.Id == both).Enabled);
    }

    [Fact]
    public async Task An_indexer_that_is_not_there_is_a_404_on_all_three_acts()
    {
        await using var application = Answering(new FakeIndexer());
        var client = await application.SignedInClientAsync();
        var absent = Guid.NewGuid();

        using var settings = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{absent}/settings",
            new { enabled = true, dailyQueryBudget = (int?)10 },
            TestContext.Current.CancellationToken);
        using var move = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{absent}/move",
            new { up = true },
            TestContext.Current.CancellationToken);
        using var preview = await client.PostAsync(
            $"/api/connections/indexers/{absent}/delete/preview",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, settings.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, move.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
    }

    private static FabApplication Answering(HttpMessageHandler indexer) =>
        new FabApplication().Answering(FabTransports.Indexers, indexer);

    private static async Task<Configured> AddAsync(HttpClient client, string url)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/connections/indexers",
            new { name = (string?)null, url, apiKey = FakeIndexer.RightKey },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await ListAsync(client)).Single(row => row.Url == url);
    }

    private static async Task<IReadOnlyList<Configured>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Configured>>(
            "/api/connections/indexers",
            TestContext.Current.CancellationToken))!;

    private static async Task<SettingsVerdict> SettingsAsync(
        HttpClient client,
        Guid id,
        bool enabled,
        int? dailyQueryBudget)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/connections/indexers/{id}/settings",
            new { enabled, dailyQueryBudget },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SettingsVerdict>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<DeletePreview> PreviewDeleteAsync(HttpClient client, Guid id)
    {
        using var response = await client.PostAsync(
            $"/api/connections/indexers/{id}/delete/preview",
            content: null,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DeletePreview>(
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<Guid> ARuleAllowing(
        HttpClient client,
        Guid indexerId,
        string name,
        params Guid[] alsoAllowing)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/settings/automation/rules",
            new
            {
                name,
                enabled = true,
                minimumSize = (long?)null,
                maximumSize = (long?)null,
                allowedIndexerIds = new[] { indexerId }.Concat(alsoAllowing).ToArray(),
            },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var verdict = (await response.Content.ReadFromJsonAsync<RuleVerdict>(
            TestContext.Current.CancellationToken))!;

        Assert.True(verdict.Saved, verdict.Detail);

        return verdict.RuleId!.Value;
    }

    private static async Task<IReadOnlyList<Rule>> RulesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AutomationSettings>(
            "/api/settings/automation",
            TestContext.Current.CancellationToken))!.Rules;

    private sealed record Configured(
        Guid Id,
        string Name,
        string Url,
        string Categories,
        bool Enabled,
        int Rank,
        int DailyQueryBudget,
        string LastVerdict,
        DateTimeOffset LastCheckedAt);

    private sealed record SettingsVerdict(bool Saved, bool Enabled, int DailyQueryBudget, string Detail);

    private sealed record DeletePreview(
        Guid IndexerId,
        string Name,
        int CachedReleases,
        int Downloads,
        int RulesReferencing,
        int RulesLosingTheirLastIndexer);

    private sealed record DeleteVerdict(Guid IndexerId, string Name, int CachedReleases, int RulesDisabled);

    private sealed record RuleVerdict(bool Saved, Guid? RuleId, string Detail);

    private sealed record AutomationSettings(IReadOnlyList<Rule> Rules);

    private sealed record Rule(Guid Id, string Name, bool Enabled);
}
