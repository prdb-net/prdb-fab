using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class IndexerCapsRoutine(
    FabDbContext context,
    NewznabGateway gateway,
    DiscoveryState discovery,
    ILogger<IndexerCapsRoutine> logger) : IRoutine, ITargetedRoutine
{
    public string Name => DiscoveryRoutineNames.Caps;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromDays(7);

    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken cancellationToken) =>
        IndexerTargets.CanonicalAsync(
            context.Indexers.Where(row => row.Enabled).Select(row => row.Id),
            cancellationToken);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId)) return RunResult.NothingToDo;
        var indexer = await context.Indexers.SingleOrDefaultAsync(row => row.Id == indexerId && row.Enabled, cancellationToken);
        if (indexer is null) return RunResult.NothingToDo;

        var read = await gateway.CapsAsync(indexer.Url, indexer.ApiKey, cancellationToken);
        if (read.Refusal is not null) return RunResult.Failed("The indexer did not provide its category tree.");

        var change = await discovery.StoreCapsAsync(indexerId, read.Categories, cancellationToken);
        logger.LogInformation(
            "The category tree from {Host} was refreshed: {Resolved} id(s), {Missing} missing name(s), {Added} newly covered id(s).",
            Host(indexer.Url), change.ResolvedIds.Count, change.MissingNames.Count, change.AddedIds.Count);
        return RunResult.Handled(change.ResolvedIds.Count);
    }

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var address) ? address.Host : "indexer";
}
