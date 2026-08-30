using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Removes expired disposable search explanations and releases their pins.</summary>
public sealed class ManualSearchRetentionRoutine(ManualSearches searches) : IRoutine
{
    public string Name => DiscoveryRoutineNames.ManualSearchRetention;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromDays(1);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var removed = await searches.DeleteExpiredAsync(cancellationToken);
        return removed == 0 ? RunResult.NothingToDo : RunResult.Handled(removed);
    }
}
