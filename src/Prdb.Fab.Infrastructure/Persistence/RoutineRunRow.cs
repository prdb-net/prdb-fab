using Prdb.Fab.Core.Scheduling;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0014's run log: the last fifty runs of a routine, read by a person in
/// the UI. Not exported — ADR 0009 takes what cannot be fetched again, and a
/// run log refetches itself by running.
/// </summary>
public sealed class RoutineRunRow
{
    public long Id { get; set; }

    public long RoutineId { get; set; }

    public RoutineRow? Routine { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset FinishedAt { get; set; }

    public RunOutcome Outcome { get; set; }

    public int ItemsHandled { get; set; }

    public int? ResultsSeen { get; set; }

    public int? RowsAdded { get; set; }

    /// <summary>
    /// A sentence for whoever reads the log, never read for control flow. ADR
    /// 0043 keeps it out of the stream's job: what asks to be fixed is a row.
    /// </summary>
    public string? Reason { get; set; }
}
