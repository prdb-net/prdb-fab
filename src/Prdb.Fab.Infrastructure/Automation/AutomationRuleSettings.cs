using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Automation;

/// <summary>Creates, changes and deletes ADR 0007's unordered permissions.</summary>
public sealed class AutomationRuleSettings(FabDbContext context, TimeProvider time)
{
    public async Task<AutomationSettingsState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var cap = await context.Installation
            .Select(row => row.AutomaticDownloadCap)
            .SingleAsync(cancellationToken);
        var indexers = await context.Indexers
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Select(row => new AutomationIndexer(row.Id, row.Name, row.Enabled))
            .ToListAsync(cancellationToken);
        var rules = await RulesAsync(cancellationToken);
        return new(cap, rules, indexers);
    }

    public async Task<AutomationRuleView?> ReadRuleAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        (await RulesAsync(cancellationToken)).SingleOrDefault(rule => rule.Id == id);

    public async Task<AutomationCapVerdict> SaveCapAsync(
        int cap,
        CancellationToken cancellationToken = default)
    {
        if (cap is < 1 or > 1000)
            return new(false, cap, 0, "The unfinished automatic Download cap must be between 1 and 1,000.");

        await context.Installation.ExecuteUpdateAsync(
            update => update.SetProperty(row => row.AutomaticDownloadCap, cap),
            cancellationToken);
        var reconsidered = await QueueCatchUpAsync(cancellationToken);
        return new(true, cap, reconsidered, "The automatic Download cap has been saved.");
    }

    public async Task<AutomationRuleVerdict> SaveRuleAsync(
        Guid? id,
        string? name,
        bool enabled,
        long? minimumSize,
        long? maximumSize,
        IReadOnlyCollection<Guid> allowedIndexerIds,
        CancellationToken cancellationToken = default)
    {
        var indexerIds = allowedIndexerIds.Distinct().ToArray();
        var indexers = await context.Indexers
            .Where(row => indexerIds.Contains(row.Id))
            .Select(row => new { row.Id, row.Enabled })
            .ToListAsync(cancellationToken);
        if (indexers.Count != indexerIds.Length)
            return AutomationRuleVerdict.Invalid(id, "One or more allowed Indexers no longer exist.");
        if (enabled && indexers.Any(indexer => !indexer.Enabled))
            return AutomationRuleVerdict.Invalid(id, "An enabled Automation Rule may use only enabled Indexers.");

        var validation = AutomationRules.Validate(name, enabled, minimumSize, maximumSize, indexerIds.Length);
        if (!validation.Accepted) return AutomationRuleVerdict.Invalid(id, validation.Detail!);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        AutomationRuleRow rule;
        if (id is { } heldId)
        {
            rule = await context.AutomationRules
                .AsTracking()
                .SingleOrDefaultAsync(row => row.Id == heldId, cancellationToken)
                ?? throw new AutomationRuleNotFoundException(heldId);
        }
        else
        {
            rule = new AutomationRuleRow { Id = Guid.CreateVersion7(time.GetUtcNow()) };
            context.AutomationRules.Add(rule);
        }

        rule.Name = name!.Trim();
        rule.Enabled = enabled;
        rule.MinimumSize = minimumSize;
        rule.MaximumSize = maximumSize;
        await context.AutomationRuleIndexers
            .Where(row => row.AutomationRuleId == rule.Id)
            .ExecuteDeleteAsync(cancellationToken);
        context.AutomationRuleIndexers.AddRange(indexerIds.Select(indexerId => new AutomationRuleIndexerRow
        {
            AutomationRuleId = rule.Id,
            IndexerId = indexerId,
        }));

        await context.SaveChangesAsync(cancellationToken);
        var reconsidered = await QueueCatchUpAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, rule.Id, reconsidered, "The Automation Rule has been saved.");
    }

    public async Task<AutomationRuleDeletePreview?> PreviewDeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var rule = await context.AutomationRules
            .AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Id, row.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (rule is null) return null;
        var origins = await context.DownloadOriginRules.CountAsync(
            row => row.AutomationRuleId == id,
            cancellationToken);
        return new(rule.Id, rule.Name, origins,
            origins == 0
                ? "The Automation Rule will be deleted."
                : $"The Automation Rule will be deleted. {origins} Download Origin member(s) keep its copied name.");
    }

    public async Task<AutomationRuleDeleteVerdict?> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewDeleteAsync(id, cancellationToken);
        if (preview is null) return null;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.AutomationRules.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken);
        var reconsidered = await QueueCatchUpAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(id, reconsidered,
            "The Automation Rule was deleted. Existing Downloads keep its copied Origin name.");
    }

    public Task<int> QueueCatchUpAsync(CancellationToken cancellationToken = default) =>
        context.Releases
            .Where(release => release.VideoId != null
                && release.IdentificationState == IdentificationState.Matched
                && context.WantedVideos.Any(wanted => wanted.VideoId == release.VideoId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(release => release.AutomationPending, true)
                .SetProperty(release => release.AutomationDecisionReason, (AutomationDecisionReason?)null),
                cancellationToken);

    private async Task<IReadOnlyList<AutomationRuleView>> RulesAsync(CancellationToken cancellationToken)
    {
        var rules = await context.AutomationRules
            .AsNoTracking()
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var edges = await context.AutomationRuleIndexers
            .AsNoTracking()
            .Include(row => row.Indexer)
            .OrderBy(row => row.Indexer!.Name)
            .ThenBy(row => row.IndexerId)
            .ToListAsync(cancellationToken);
        return rules.Select(rule => new AutomationRuleView(
            rule.Id,
            rule.Name,
            rule.Enabled && edges.Any(edge => edge.AutomationRuleId == rule.Id && edge.Indexer?.Enabled == true),
            rule.MinimumSize,
            rule.MaximumSize,
            [.. edges.Where(edge => edge.AutomationRuleId == rule.Id)
                .Select(edge => new AutomationIndexer(edge.IndexerId, edge.Indexer?.Name ?? "Deleted Indexer", edge.Indexer?.Enabled ?? false))]))
            .ToArray();
    }
}

public sealed class AutomationRuleNotFoundException(Guid id)
    : Exception($"Automation Rule {id:D} does not exist.");

public sealed record AutomationSettingsState(
    int AutomaticDownloadCap,
    IReadOnlyList<AutomationRuleView> Rules,
    IReadOnlyList<AutomationIndexer> Indexers);
public sealed record AutomationIndexer(Guid Id, string Name, bool Enabled);
public sealed record AutomationRuleView(
    Guid Id,
    string Name,
    bool Enabled,
    long? MinimumSize,
    long? MaximumSize,
    IReadOnlyList<AutomationIndexer> AllowedIndexers);
public sealed record AutomationCapVerdict(bool Saved, int AutomaticDownloadCap, int Reconsidered, string Detail);
public sealed record AutomationRuleVerdict(bool Saved, Guid? RuleId, int Reconsidered, string Detail)
{
    public static AutomationRuleVerdict Invalid(Guid? id, string detail) => new(false, id, 0, detail);
}
public sealed record AutomationRuleDeletePreview(Guid RuleId, string Name, int ExistingOrigins, string Detail);
public sealed record AutomationRuleDeleteVerdict(Guid RuleId, int Reconsidered, string Detail);
