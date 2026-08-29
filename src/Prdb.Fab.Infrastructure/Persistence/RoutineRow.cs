using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0014's routine table, and under ADR 0038 the only truth about what is
/// due. <em>Run now</em> changes <see cref="DueAt"/> here and records its visible
/// verdict beside the same scheduler fact.
/// </summary>
public sealed class RoutineRow
{
    public long Id { get; set; }

    /// <summary>Binds the row to its code. See <see cref="IRoutine.Name"/>.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// What this row is <em>about</em> — one indexer of several, say — or null
    /// for a routine that exists once. Not a second lookup: it is handed to the
    /// routine as an argument.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Which lane's worker turns this. Stored as its name rather than as a
    /// number, so the table reads as something rather than as a column of
    /// threes.
    /// </summary>
    public Lane Lane { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    /// <summary>
    /// Three of these are ADR 0014's Gap. An interrupted run does not move it,
    /// and neither does an empty tick, because neither is evidence about
    /// whether the thing works.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>The latest scheduler deferral, retained as a Status Brake.</summary>
    public DateTimeOffset? LastDeferredAt { get; set; }

    public DateTimeOffset? DeferredUntil { get; set; }

    public string? LastDeferredReason { get; set; }

    /// <summary>The latest person's Run now request and what became of it.</summary>
    public DateTimeOffset? LastRunNowAt { get; set; }

    public RunNowOutcome? LastRunNowOutcome { get; set; }

    public string? LastRunNowDetail { get; set; }

    public bool RunNowPending { get; set; }
}
