namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// Scaffolding, and deliberately named so it cannot be mistaken for a concept.
/// </summary>
/// <remarks>
/// The walking skeleton needs one thing a routine can work through and the one
/// route can show, and <c>VISION.md</c>'s loop is out of scope here — so this
/// is a row with a label and a stamp saying whether the sweep has seen it. It
/// is not in <c>CONTEXT.md</c>, it is not exported, and it leaves with the
/// first real feature.
/// </remarks>
public sealed class SkeletonItemRow
{
    public long Id { get; set; }

    public required string Label { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    /// <summary>
    /// Null until the sweep has been past. The set of rows where this is null
    /// is the routine's work set, which is what makes ADR 0032's rule
    /// observable: empty set, not due, no run recorded.
    /// </summary>
    public DateTimeOffset? SweptAt { get; set; }
}
