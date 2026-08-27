using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;

namespace Prdb.Fab.Infrastructure.Tests.Sync;

/// <summary>
/// A routine that spends prdb requests on a clock, which is what makes it
/// something ADR 0014 may shed.
/// </summary>
/// <remarks>
/// It stands in for the actors feed and stands in honestly: same kind of work,
/// same cadence, reached through <see cref="PrdbGateway"/> the same way. The
/// actors feed itself would need a page of JSON per run to say the same thing,
/// and what these tests are about is the schedule rather than the feed.
/// </remarks>
internal sealed class ShedRoutine(PrdbGateway prdb) : IRoutine, ISpendsPrdbBudget
{
    public const string RoutineName = "test.shed-me";

    public const string ApiKey = "0123456789abcdef0123456789abcdef";

    public string Name => RoutineName;

    public Lane Lane => Lane.Sync;

    /// <summary>ADR 0014's cadence for the actors feed, which is what this is.</summary>
    public TimeSpan Cadence => TimeSpan.FromHours(6);

    /// <summary>The first thing given up under a plan too small: shed to daily.</summary>
    public PrdbWork Spends => PrdbWork.Actors;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var identity = await prdb.AskAsync(
            ApiKey,
            Spends,
            (client, token) => client.UserIdentity.GetAsync(cancellationToken: token),
            cancellationToken);

        return RunResult.Handled(identity is null ? 0 : 1);
    }
}
