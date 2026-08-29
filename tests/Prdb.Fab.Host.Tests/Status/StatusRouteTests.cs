using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Status;

public sealed class StatusRouteTests
{
    [Fact]
    public async Task Status_is_the_six_stage_local_loop()
    {
        await using var application = new FabApplication();
        var client = await application.SignedInClientAsync();

        var status = await client.GetFromJsonAsync<State>(
            "/api/status",
            TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal(
            ["sync-prdb", "sync-indexers", "match", "decide", "download", "file"],
            status.Stages.Select(stage => stage.Id).ToArray());
        Assert.All(status.Stages.SelectMany(stage => stage.Routines), routine =>
        {
            Assert.NotEqual(default, routine.DueAt);
            Assert.False(string.IsNullOrWhiteSpace(routine.Name));
        });
        Assert.Contains(status.Related, link => link.Route == "/downloads");
        Assert.Contains(status.Related, link => link.Route == "/review-queue");
        Assert.Contains(status.Related, link => link.Route == "/operation-log");
    }

    private sealed record State(int GapCount, IReadOnlyList<Stage> Stages, IReadOnlyList<Link> Related);
    private sealed record Stage(string Id, IReadOnlyList<Routine> Routines);
    private sealed record Routine(string Name, DateTimeOffset DueAt);
    private sealed record Link(string Label, string Route);
}
