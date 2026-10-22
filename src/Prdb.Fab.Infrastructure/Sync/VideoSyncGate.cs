namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Serializes the baseline and its change-feed handoff across lanes.</summary>
public sealed class VideoSyncGate
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
