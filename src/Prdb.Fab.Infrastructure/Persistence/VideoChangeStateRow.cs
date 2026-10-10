namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>Proof that local Catalogue details are covered by the Video feed.</summary>
public sealed class VideoChangeStateRow
{
    public int Id { get; set; } = 1;
    public DateTimeOffset LowerBoundAt { get; set; }
    public DateTimeOffset? BaselineCompletedAt { get; set; }
    public DateTimeOffset? CaughtUpAt { get; set; }
    public DateTimeOffset? CaughtUpObservedAt { get; set; }
}
