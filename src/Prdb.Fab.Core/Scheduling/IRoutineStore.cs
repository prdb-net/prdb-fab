namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// The routine row, which ADR 0038 made the only truth about what is due.
/// Implemented in <c>Prdb.Fab.Infrastructure</c>; declared here because the
/// schedule is a rule and the rows are not.
/// </summary>
public interface IRoutineStore
{
    /// <summary>
    /// The routines in <paramref name="lane"/> whose next-due time has passed,
    /// in the order the lane should take them.
    /// </summary>
    /// <remarks>
    /// Asked on every tick rather than cached, which is the point of ADR 0038's
    /// rule: <em>run now</em> is a write to this row and nothing else, so a
    /// worker holding its own idea of what is due would be a second truth and
    /// the one control a person has would be the one control that does not work.
    /// </remarks>
    Task<IReadOnlyList<DueRoutine>> DueAsync(Lane lane, CancellationToken cancellationToken);

    /// <summary>
    /// Writes down what a run did and when the routine may next be due.
    /// </summary>
    /// <remarks>
    /// A <paramref name="result"/> that is not recorded moves the due time on
    /// and writes no run — ADR 0032's empty tick, applied in one place because
    /// <see cref="RunResult"/> puts it in one place.
    /// </remarks>
    Task RecordAsync(long routineId, RunResult result, TimeSpan cadence, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a routine due now through its existing schedule row.
    /// </summary>
    /// <remarks>
    /// ADR 0038's <em>run now</em>, and the reason the row is the only truth:
    /// this is one atomic row update, the lane picks it up on its next tick,
    /// and a forced run is therefore governed and deferred like any other. Its
    /// visible request verdict is retained on that row. A second path
    /// that called the routine directly would be the one control a person has
    /// bypassing the one control that holds the rate limit.
    /// </remarks>
    /// <returns>Whether the request was accepted.</returns>
    Task<bool> RunNowAsync(string name, string? target, CancellationToken cancellationToken);

    /// <summary>Makes a routine due and returns the durable decision shown by Status.</summary>
    Task<RunNowVerdict> RunNowDetailedAsync(string name, string? target, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the row of a routine that has finished. See <see cref="IOneShot"/>.
    /// </summary>
    /// <remarks>
    /// Keyed by name and target rather than by id, the same way
    /// <see cref="RunNowAsync"/> is, because a routine knows what it is and not
    /// which row it was read from. It takes its run log with it, which is the
    /// honest consequence of ADR 0014 making the row the thing that exists: a
    /// bootstrap that has retired is not a routine with an empty history, it is
    /// not a routine.
    /// </remarks>
    /// <returns>Whether there was a row to remove.</returns>
    Task<bool> RetireAsync(string name, string? target, CancellationToken cancellationToken);
}
