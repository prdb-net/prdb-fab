using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests;

/// <summary>
/// The one route, end to end, against the application as <c>Program.cs</c>
/// composes it.
/// </summary>
/// <remarks>
/// ADR 0042: the wiring is the part worth testing, which is why nothing here is
/// replaced by a double. The database is a real SQLite file, migrated at
/// startup exactly as it is in the container, and the lane is turning
/// underneath the whole time.
/// </remarks>
/// <remarks>
/// The client is signed in, because ADR 0010 puts everything behind the
/// password. What that refusal looks like is <c>AccessRouteTests</c>'s
/// question, not this one's.
/// </remarks>
public sealed class SkeletonRouteTests(FabApplication application) : IClassFixture<FabApplication>
{
    [Fact]
    public async Task The_health_route_answers()
    {
        using var client = await application.SignedInClientAsync();

        using var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_api_path_is_not_the_frontend()
    {
        using var client = await application.SignedInClientAsync();

        using var response = await client.GetAsync(
            "/api/there-is-no-such-thing", TestContext.Current.CancellationToken);

        // ADR 0036: unknown paths are the frontend's, unknown API paths are not.
        // A caller that asked a question the API does not have gets that answer
        // rather than a page.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_item_can_be_added_and_read_back()
    {
        using var client = await application.SignedInClientAsync();

        var verdict = await PostAsync<AddItemVerdict>(client, "/api/skeleton/items", new { label = "a thing" });

        Assert.Null(verdict.Refusal);
        Assert.NotNull(verdict.Added);
        Assert.Equal("a thing", verdict.Added!.Label);

        var page = await client.GetFromJsonAsync<ItemPage>(
            "/api/skeleton/items?page=1", TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Contains(page!.Items, item => item.Label == "a thing");
    }

    /// <summary>
    /// ADR 0040: a verdict is HTTP 200 with a typed body. A refusal is something
    /// the caller reads, not a status code TanStack Query would retry.
    /// </summary>
    [Fact]
    public async Task A_refusal_is_a_success_with_a_reason()
    {
        using var client = await application.SignedInClientAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/skeleton/items", new { label = "   " }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var verdict = await response.Content.ReadFromJsonAsync<AddItemVerdict>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(verdict);
        Assert.Null(verdict!.Added);
        Assert.NotNull(verdict.Refusal);
    }

    /// <summary>
    /// The skeleton walking: an act reaches the database, the bulk lane picks
    /// the work up on its own within a tick or two, and the run appears in the
    /// log. Nothing here calls the routine.
    /// </summary>
    [Fact]
    public async Task The_lane_sweeps_what_the_route_added()
    {
        using var client = await application.SignedInClientAsync();

        await PostAsync<AddItemVerdict>(client, "/api/skeleton/items", new { label = "for the lane" });

        // ADR 0038: run now is one write to the row. The lane finds it.
        await PostAsync<RunNowVerdict>(client, "/api/skeleton/sweep/run-now", new { });

        var swept = await EventuallyAsync(async () =>
        {
            var page = await client.GetFromJsonAsync<ItemPage>(
                "/api/skeleton/items?page=1", TestContext.Current.CancellationToken);

            return page!.Items.Any(item => item.Label == "for the lane" && item.SweptAt is not null);
        });

        Assert.True(swept, "the bulk lane did not sweep the item within the timeout");

        var runs = await client.GetFromJsonAsync<RecordedRun[]>(
            "/api/skeleton/runs", TestContext.Current.CancellationToken);

        Assert.NotEmpty(runs!);
        Assert.Contains(runs!, run => run.Outcome == "Succeeded");
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// The lane runs on the real clock — it is the composed application, and
    /// ADR 0042's FakeTimeProvider belongs where a unit is tested rather than
    /// where the whole thing is. So this waits for a fact rather than for a
    /// duration, and gives up loudly.
    /// </summary>
    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private sealed record SkeletonItem(long Id, string Label, DateTimeOffset AddedAt, DateTimeOffset? SweptAt);

    private sealed record ItemPage(IReadOnlyList<SkeletonItem> Items, int Page, int PageSize, int Total);

    private sealed record AddItemVerdict(SkeletonItem? Added, string? Refusal);

    private sealed record RunNowVerdict(bool Accepted, string Detail);

    private sealed record RecordedRun(long Id, string RoutineName, DateTimeOffset StartedAt, string Outcome, int ItemsHandled, string? Reason);
}
