using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class DiscoveryState(FabDbContext context, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitialiseAsync(
        Guid indexerId,
        IReadOnlyList<CapsCategory> caps,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var indexer = await context.Indexers.SingleAsync(row => row.Id == indexerId, cancellationToken);
        var resolution = Resolve(indexer.Categories, caps);

        context.IndexerWalkStates.Add(new IndexerWalkStateRow
        {
            IndexerId = indexerId,
            CapsTree = JsonSerializer.Serialize(caps, Json),
            ResolvedCategoryIds = JsonSerializer.Serialize(resolution.Ids, Json),
            MissingCategoryNames = JsonSerializer.Serialize(resolution.MissingNames, Json),
            CapsCheckedAt = now,
            QueryDay = StartOfDay(now),
            ResumePage = 0,
        });

        await EnsureRoutineAsync(DiscoveryRoutineNames.Caps, Lane.Sync, indexerId, now, cancellationToken);
        await EnsureRoutineAsync(DiscoveryRoutineNames.Walk, Lane.Sync, indexerId, now, cancellationToken);
        await EnsureRoutineAsync(DiscoveryRoutineNames.Bootstrap, Lane.Bulk, indexerId, now, cancellationToken);
        await EnsureRoutineAsync(
            DiscoveryRoutineNames.WantedSweep,
            Lane.Sync,
            indexerId,
            now,
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gives upgraded indexers their cache half, records every discovery
    /// routine row and activates foundation rows left dormant by an earlier
    /// release before their implementations arrived.
    /// </summary>
    public async Task EnsureFoundationAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var indexers = await context.Indexers.ToListAsync(cancellationToken);

        foreach (var indexer in indexers)
        {
            if (!await context.IndexerWalkStates.AnyAsync(row => row.IndexerId == indexer.Id, cancellationToken))
            {
                var configured = indexer.Categories.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                context.IndexerWalkStates.Add(new IndexerWalkStateRow
                {
                    IndexerId = indexer.Id,
                    MissingCategoryNames = JsonSerializer.Serialize(configured, Json),
                    QueryDay = StartOfDay(now),
                    ResumePage = 0,
                });
            }

            if (indexer.Enabled)
            {
                await EnsureRoutineAsync(DiscoveryRoutineNames.Caps, Lane.Sync, indexer.Id, now, cancellationToken);
                await EnsureRoutineAsync(DiscoveryRoutineNames.Walk, Lane.Sync, indexer.Id, now, cancellationToken);
                await EnsureRoutineAsync(DiscoveryRoutineNames.Bootstrap, Lane.Bulk, indexer.Id, now, cancellationToken);
                await EnsureRoutineAsync(DiscoveryRoutineNames.WantedSweep, Lane.Sync, indexer.Id, now, cancellationToken);
            }
        }

        await EnsureGlobalRoutineAsync(DiscoveryRoutineNames.Screening, Lane.Bulk, now, cancellationToken);
        await EnsureGlobalRoutineAsync(DiscoveryRoutineNames.BackwardsSearch, Lane.Bulk, now, cancellationToken);
        await EnsureGlobalRoutineAsync(DiscoveryRoutineNames.Identification, Lane.Sync, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var enabledTargets = indexers
            .Where(indexer => indexer.Enabled)
            .Select(indexer => indexer.Id.ToString("D"))
            .ToArray();
        var activatedNames = new[]
        {
            DiscoveryRoutineNames.Screening,
            DiscoveryRoutineNames.BackwardsSearch,
            DiscoveryRoutineNames.Identification,
        };

        // These exact never-run rows are the dormant foundation written by
        // the release-discovery schema cut. A row with any history is not that
        // foundation state and remains untouched, including one deliberately
        // stopped after a failure.
        await context.Routines
            .Where(row => row.DueAt == DateTimeOffset.MaxValue
                && row.LastSuccessAt == null
                && row.LastFailureAt == null
                && row.ConsecutiveFailures == 0
                && ((row.Target == null && activatedNames.Contains(row.Name))
                    || (row.Name == DiscoveryRoutineNames.WantedSweep
                        && row.Target != null
                        && enabledTargets.Contains(row.Target))))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.DueAt, now), cancellationToken);
    }

    public async Task<CapsChange> StoreCapsAsync(
        Guid indexerId,
        IReadOnlyList<CapsCategory> caps,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var indexer = await context.Indexers.AsTracking().SingleAsync(row => row.Id == indexerId, cancellationToken);
        var state = await context.IndexerWalkStates.AsTracking().SingleAsync(row => row.IndexerId == indexerId, cancellationToken);
        var oldIds = DeserialiseIds(state.ResolvedCategoryIds);
        var resolution = Resolve(indexer.Categories, caps);
        // Renumbering changes every id while preserving the same coverage. A
        // catch-up is needed only when the resolved set grew, not merely when
        // the indexer changed the numbers attached to the same names.
        var addedIds = resolution.Ids.Count > oldIds.Count
            ? resolution.Ids.Except(oldIds).ToArray()
            : [];

        state.CapsTree = JsonSerializer.Serialize(caps, Json);
        state.ResolvedCategoryIds = JsonSerializer.Serialize(resolution.Ids, Json);
        state.MissingCategoryNames = JsonSerializer.Serialize(resolution.MissingNames, Json);
        state.CapsCheckedAt = now;
        indexer.LastCheckedAt = now;
        indexer.LastVerdict = resolution.MissingNames.Count == 0
            ? IndexerConnectionOutcome.Saved
            : IndexerConnectionOutcome.NoCategories;

        if (addedIds.Length > 0 && state.BootstrapCompletedAt is not null)
        {
            OpenCatchUp(state, now - TimeSpan.FromDays(90), now, "category extension");
            await EnsureRoutineAsync(DiscoveryRoutineNames.CatchUp, Lane.Bulk, indexerId, now, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        return new(resolution.Ids, resolution.MissingNames, addedIds);
    }

    public async Task OpenCatchUpAsync(
        Guid indexerId,
        DateTimeOffset from,
        DateTimeOffset to,
        string cause,
        CancellationToken cancellationToken)
    {
        var state = await context.IndexerWalkStates.AsTracking().SingleAsync(row => row.IndexerId == indexerId, cancellationToken);
        OpenCatchUp(state, from, to, cause);
        await EnsureRoutineAsync(DiscoveryRoutineNames.CatchUp, Lane.Bulk, indexerId, time.GetUtcNow(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<int> DeserialiseIds(string json) =>
        JsonSerializer.Deserialize<int[]>(json, Json) ?? [];

    public static IReadOnlyList<string> DeserialiseNames(string json) =>
        JsonSerializer.Deserialize<string[]>(json, Json) ?? [];

    private async Task EnsureRoutineAsync(
        string name,
        Lane lane,
        Guid indexerId,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        var target = indexerId.ToString("D");
        if (!context.Routines.Local.Any(row => row.Name == name && row.Target == target)
            && !await context.Routines.AnyAsync(row => row.Name == name && row.Target == target, cancellationToken))
        {
            context.Routines.Add(new RoutineRow { Name = name, Target = target, Lane = lane, DueAt = dueAt });
        }
    }

    private async Task EnsureGlobalRoutineAsync(
        string name,
        Lane lane,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        if (!await context.Routines.AnyAsync(row => row.Name == name && row.Target == null, cancellationToken))
        {
            context.Routines.Add(new RoutineRow
            {
                Name = name,
                Target = null,
                Lane = lane,
                DueAt = dueAt,
            });
        }
    }

    private static void OpenCatchUp(
        IndexerWalkStateRow state,
        DateTimeOffset from,
        DateTimeOffset to,
        string cause)
    {
        state.CatchUpFrom = state.CatchUpFrom is null || from < state.CatchUpFrom ? from : state.CatchUpFrom;
        state.CatchUpTo = state.CatchUpTo is null || to > state.CatchUpTo ? to : state.CatchUpTo;
        state.ResumePage = 0;
        state.CatchUpCause = cause;
    }

    private static CategoryResolution Resolve(string configured, IReadOnlyList<CapsCategory> caps)
    {
        var requested = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var nodes = Flatten(caps, parent: null).ToArray();
        var ids = new HashSet<int>();
        var missing = new List<string>();

        foreach (var name in requested.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = nodes.Where(node => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                missing.Add(name);
                continue;
            }

            foreach (var match in matches)
            {
                ids.Add(match.Category.Id);
                AddDescendants(match.Category, ids);
            }
        }

        return new([.. ids.Order()], [.. missing.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    private static IEnumerable<CategoryNode> Flatten(IReadOnlyList<CapsCategory> categories, string? parent)
    {
        foreach (var category in categories)
        {
            var name = parent is null ? category.Name : $"{parent}/{category.Name}";
            yield return new(name, category);
            foreach (var child in Flatten(category.Children, name)) yield return child;
        }
    }

    private static void AddDescendants(CapsCategory category, HashSet<int> ids)
    {
        foreach (var child in category.Children)
        {
            ids.Add(child.Id);
            AddDescendants(child, ids);
        }
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset value) =>
        new(value.UtcDateTime.Date, TimeSpan.Zero);

    private sealed record CategoryNode(string Name, CapsCategory Category);
    private sealed record CategoryResolution(IReadOnlyList<int> Ids, IReadOnlyList<string> MissingNames);
}

public sealed record CapsChange(
    IReadOnlyList<int> ResolvedIds,
    IReadOnlyList<string> MissingNames,
    IReadOnlyList<int> AddedIds);
