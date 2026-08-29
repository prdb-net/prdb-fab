using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Converges durable account intent through prdb's governed write lane.</summary>
public sealed class AccountPreferenceRoutine(
    FabDbContext context,
    AccountPreferences preferences) : IRoutine
{
    public const string RoutineName = "prdb.account-preferences";
    public const int BatchSize = 25;

    public string Name => RoutineName;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var pending = await context.AccountPreferenceWrites
            .AsNoTracking()
            .Where(row => !row.Blocked)
            .OrderBy(row => row.RequestedAt)
            .ThenBy(row => row.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0) return RunResult.NothingToDo;

        var handled = 0;
        foreach (var write in pending)
        {
            var verdict = await preferences.CompleteAsync(write, cancellationToken);
            if (verdict.Outcome == AccountPreferenceOutcome.Deferred)
            {
                throw new PrdbDeferredException(
                    PrdbWork.Writes,
                    TimeSpan.FromSeconds(verdict.RetryAfterSeconds ?? 1),
                    verdict.Detail);
            }
            if (verdict.Outcome == AccountPreferenceOutcome.Failed)
            {
                return handled == 0
                    ? RunResult.Failed(verdict.Detail)
                    : RunResult.Handled(handled, verdict.Detail);
            }
            handled++;
        }

        return RunResult.Handled(handled);
    }
}
