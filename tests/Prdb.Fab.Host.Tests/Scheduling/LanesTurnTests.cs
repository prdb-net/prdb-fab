using System.Net.Http.Json;

using Prdb.Fab.Host.Tests.Connections;
using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Host.Tests.Scheduling;

/// <summary>
/// ADR 0038 gives each lane a worker of its own, and this asks the one question
/// that composition cannot be trusted with: whether they all turn.
/// </summary>
/// <remarks>
/// A routine in a lane nothing turns never runs, and nothing anywhere says so —
/// no failure, no Gap, no line in the log. That is the shape ADR 0018 cannot
/// draw, and it is one registration away at all times: every lane is the same
/// class with a different argument, so the obvious way to add the second one
/// silently keeps the first.
/// </remarks>
public sealed class LanesTurnTests
{
    private const string AKey = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// The sync lane, from the outside: a key is saved through ADR 0010's own
    /// route, and prdb is then asked something nobody asked for — which is only
    /// possible if the lane is turning and the feeds have rows.
    /// </summary>
    [Fact]
    public async Task The_sync_lane_turns_and_asks_prdb_on_its_own()
    {
        var prdb = new FakePrdb();

        await using var application = new FabApplication().Answering(FabTransports.Prdb, prdb);

        using var client = await application.SignedInClientAsync();

        using var saved = await client.PostAsJsonAsync(
            "/api/connections/prdb",
            new { apiKey = AKey, confirmAnotherAccount = false },
            TestContext.Current.CancellationToken);

        saved.EnsureSuccessStatusCode();

        // Everything up to here went through /user-identity. Anything else is
        // the schedule, and the drain is what reaches prdb first because
        // ADR 0032's idle tick is the shortest cadence in the lane.
        var asked = await EventuallyAsync(() =>
            prdb.Paths.Any(path => path != "/user-identity"));

        Assert.True(asked, "the sync lane did not reach prdb on its own within the timeout");
    }

    /// <summary>
    /// Long enough for the restart spread and a cadence or two, short enough
    /// that a lane which is not turning fails rather than hangs.
    /// </summary>
    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        var deadline = TimeSpan.FromSeconds(30);
        var waited = TimeSpan.Zero;
        var step = TimeSpan.FromMilliseconds(100);

        while (waited < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(step, TestContext.Current.CancellationToken);
            waited += step;
        }

        return condition();
    }
}
