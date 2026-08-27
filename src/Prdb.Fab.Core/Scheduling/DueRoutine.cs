namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// A routine row, as much of it as a lane worker needs. ADR 0035 has
/// <c>Core</c> see narrow projections rather than the persistence model.
/// </summary>
public sealed record DueRoutine(long Id, string Name, string? Target);
