using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Refresh the displayed subscription through the same budget as other account work.</summary>
public sealed class PrdbAccountRoutine(FabDbContext context, PrdbGateway prdb, PrdbGovernor governor) : IRoutine, ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.account";
    public string Name => RoutineName;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromDays(1);
    public PrdbWork Spends => PrdbWork.UserFeeds;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var key = await context.Installation.Select(row => row.PrdbApiKey).SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(key)) return RunResult.NothingToDo;
        var identity = await prdb.AskAsync(key, Spends,
            async (client, token) =>
            {
                var answer = await client.UserIdentity.GetAsync(cancellationToken: token);
                if (!string.IsNullOrWhiteSpace(answer?.UserHash)) governor.ObserveIdentity(answer);
                return answer;
            }, cancellationToken);
        if (string.IsNullOrWhiteSpace(identity?.UserHash))
            return RunResult.Failed("prdb returned no account identity.");
        return RunResult.Handled(1);
    }
}
