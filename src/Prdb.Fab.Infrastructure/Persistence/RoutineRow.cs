using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0014's routine table, and under ADR 0038 the only truth about what is
/// due. <em>Run now</em> is one write to <see cref="DueAt"/> and nothing else.
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
}
