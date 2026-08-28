namespace Prdb.Fab.Core.Scheduling;

/// <summary>A routine whose rows are discovered from durable targets at registration time.</summary>
public interface ITargetedRoutine
{
    Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken);
}
