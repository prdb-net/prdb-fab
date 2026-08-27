using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Scheduling;

/// <summary>One recorded run, as a person reads it.</summary>
public sealed record RecordedRun(
    long Id,
    string RoutineName,
    DateTimeOffset StartedAt,
    RunOutcome Outcome,
    int ItemsHandled,
    string? Reason);

/// <summary>
/// Reads ADR 0014's run log. The read side only — writing is
/// <see cref="RoutineStore"/>'s, because a run is recorded as part of finishing
/// it rather than as a separate act.
/// </summary>
public sealed class RunLog(FabDbContext context)
{
    public async Task<IReadOnlyList<RecordedRun>> RecentAsync(
        string routineName,
        int count,
        CancellationToken cancellationToken)
    {
        return await context.RoutineRuns
            .Where(row => row.Routine!.Name == routineName)
            .OrderByDescending(row => row.StartedAt)
            .ThenByDescending(row => row.Id)
            .Take(count)
            .Select(row => new RecordedRun(
                row.Id,
                row.Routine!.Name,
                row.StartedAt,
                row.Outcome,
                row.ItemsHandled,
                row.Reason))
            .ToListAsync(cancellationToken);
    }
}
