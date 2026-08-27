using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// A routine that asks prdb one question, which is all the governor needs to be
/// exercised.
/// </summary>
/// <remarks>
/// It stands in for the four that arrive with the feeds, and it stands in
/// honestly: it reaches prdb through <see cref="PrdbGateway"/> like they will,
/// says what its request is for like they will, and catches nothing — because
/// what a run <em>was</em> is the lane's to decide and not a routine's.
/// </remarks>
internal sealed class GovernedRoutine(PrdbGateway prdb) : IRoutine
{
    public const string RoutineName = "test.asks-prdb";

    /// <summary>
    /// Not the top of ADR 0014's order and not the bottom: what is interesting
    /// about a user feed is that it is held back long before an arrived file is
    /// and long after repair is.
    /// </summary>
    public const PrdbWork Work = PrdbWork.UserFeeds;

    public const string ApiKey = "0123456789abcdef0123456789abcdef";

    public string Name => RoutineName;

    /// <summary>Where ADR 0014 puts the prdb feeds.</summary>
    public Lane Lane => Lane.Sync;

    /// <summary>ADR 0014's cadence for the three user feeds.</summary>
    public TimeSpan Cadence => TimeSpan.FromMinutes(60);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var identity = await prdb.AskAsync(
            ApiKey,
            Work,
            (client, token) => client.UserIdentity.GetAsync(cancellationToken: token),
            cancellationToken);

        return RunResult.Handled(identity is null ? 0 : 1);
    }
}
