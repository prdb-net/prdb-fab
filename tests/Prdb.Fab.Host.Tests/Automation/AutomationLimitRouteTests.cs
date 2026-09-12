using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Automation;

/// <summary>
/// ADR 0020's two Automation limits: the cap on unfinished automatic Downloads,
/// and the per-Video retry budget — which had no field at all until ADR 0058,
/// although the Release view read it and showed a Video's spent attempts
/// against it.
/// </summary>
public sealed class AutomationLimitRouteTests
{
    [Fact]
    public async Task The_retry_budget_starts_at_three_and_can_be_set()
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        Assert.Equal(3, (await ReadAsync(client)).RetryBudget);

        var verdict = await SaveAsync(client, 5);

        Assert.True(verdict.Saved);
        Assert.Equal(5, verdict.RetryBudget);
        Assert.Equal(5, (await ReadAsync(client)).RetryBudget);
    }

    /// <summary>
    /// ADR 0020's own reasoning is the bound: five attempts are absurd against
    /// one Indexer and three are thin against four, and the tool cannot see
    /// which case it is in — so the range admits both and refuses what is
    /// neither.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(1000)]
    public async Task A_budget_outside_the_range_is_refused_and_changes_nothing(int budget)
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        var verdict = await SaveAsync(client, budget);

        Assert.False(verdict.Saved);
        Assert.Contains("between 1 and 10", verdict.Detail, StringComparison.Ordinal);
        Assert.Equal(3, (await ReadAsync(client)).RetryBudget);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Both_ends_of_the_range_are_accepted(int budget)
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        Assert.True((await SaveAsync(client, budget)).Saved);
        Assert.Equal(budget, (await ReadAsync(client)).RetryBudget);
    }

    /// <summary>
    /// Raising it reconsiders, the way the cap does. A Video that stopped
    /// because its budget was spent has work to do again the moment there is
    /// more of it, and leaving that until something else happened to queue it
    /// would make the setting look like it did nothing.
    /// </summary>
    [Fact]
    public async Task Lowering_it_reconsiders_nothing()
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        await SaveAsync(client, 6);

        var lowered = await SaveAsync(client, 2);

        Assert.True(lowered.Saved);
        Assert.Equal(0, lowered.Reconsidered);
        Assert.Contains("Nothing already downloaded is affected", lowered.Detail, StringComparison.Ordinal);
    }

    private static async Task<Settings> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Settings>(
            "/api/settings/automation",
            TestContext.Current.CancellationToken))!;

    private static async Task<BudgetVerdict> SaveAsync(HttpClient client, int budget)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/settings/automation/retry-budget",
            new { retryBudget = budget },
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BudgetVerdict>(
            TestContext.Current.CancellationToken))!;
    }

    private sealed record Settings(int AutomaticDownloadCap, int RetryBudget);

    private sealed record BudgetVerdict(bool Saved, int RetryBudget, int Reconsidered, string Detail);
}
