namespace Prdb.Fab.Core.Skeleton;

/// <summary>
/// The rule the skeleton's one routine follows. It is here rather than beside
/// the code that reads rows because ADR 0035 puts rules in <c>Core</c>, and
/// because a bound on a run is a rule: ADR 0014 wants runs bounded so that a
/// long backlog is many short runs rather than one that holds the lane.
/// </summary>
/// <remarks>
/// This whole namespace is scaffolding and says so. It exists to give the
/// schedule something to schedule and the one route something to show, and it
/// goes when the first real feature arrives — nothing in it is part of
/// <c>VISION.md</c>'s loop.
/// </remarks>
public static class SkeletonSweep
{
    /// <summary>The most one run will take, however long the backlog is.</summary>
    public const int ItemsPerRun = 20;

    /// <summary>The routine's stable name, as the row carries it.</summary>
    public const string RoutineName = "skeleton-sweep";
}
