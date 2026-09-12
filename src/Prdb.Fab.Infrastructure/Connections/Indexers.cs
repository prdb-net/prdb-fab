using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0010's skippable search step: indexers, added one at a time, each its
/// own row.
/// </summary>
/// <remarks>
/// One row each from the start, because ADR 0002 identifies a release by the
/// indexer together with that indexer's own id for it — so the row's identity is
/// load-bearing before anything is ever searched.
/// </remarks>
public sealed class Indexers(
    FabDbContext context,
    NewznabGateway newznab,
    DiscoveryState discovery,
    TimeProvider time,
    ILogger<Indexers> logger)
{
    /// <summary>
    /// Every configured indexer, in ADR 0008's order.
    /// </summary>
    /// <remarks>
    /// By rank rather than by name, because the rank <em>is</em> the list
    /// position: ADR 0020 chose a position over a typed number, so a list shown
    /// in any other order would be showing something that is not the setting.
    /// The name breaks a tie, which two rows can only have while one of them is
    /// mid-move.
    /// </remarks>
    public async Task<IReadOnlyList<ConfiguredIndexer>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Indexers
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Name)
            .Select(row => new ConfiguredIndexer(
                row.Id,
                row.Name,
                row.Url,
                row.Categories,
                row.Enabled,
                row.Rank,
                row.DailyQueryBudget,
                row.LastVerdict,
                row.LastCheckedAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Checks an indexer with a real search, reads its category tree, and adds
    /// it. Nothing is written past a failure.
    /// </summary>
    public async Task<IndexerSave> AddAsync(
        string? name,
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var address = (url ?? string.Empty).Trim();

        if (await context.Indexers.AnyAsync(row => row.Url == address, cancellationToken))
        {
            return new IndexerSave(IndexerConnectionOutcome.AlreadyAdded, null, []);
        }

        var check = await newznab.CheckAsync(address, apiKey, cancellationToken);

        if (check.Outcome is not IndexerConnectionOutcome.Saved)
        {
            return new IndexerSave(check.Outcome, check.Said, []);
        }

        var categories = IndexerConnection.MatchedByName(check.Categories);

        if (categories.Count == 0)
        {
            return new IndexerSave(IndexerConnectionOutcome.NoCategories, null, []);
        }

        var now = time.GetUtcNow();

        var indexer = new IndexerRow
        {
            // ADR 0033's UUIDv7, and given the injected clock rather than the
            // ambient one — ADR 0042 has no exception for a value that happens
            // to be shaped like an identifier.
            Id = Guid.CreateVersion7(now),
            Name = string.IsNullOrWhiteSpace(name) ? HostOf(address) : name.Trim(),
            Url = address,
            ApiKey = (apiKey ?? string.Empty).Trim(),
            Categories = string.Join(',', categories),
            LastVerdict = IndexerConnectionOutcome.Saved,
            LastCheckedAt = now,
            Enabled = true,
            Rank = await context.Indexers.MaxAsync(row => (int?)row.Rank, cancellationToken) + 1 ?? 0,
            DailyQueryBudget = 1000,
        };

        context.Indexers.Add(indexer);

        // The first indexer closes the Gap that skipping the search step left,
        // wherever in the installation's life it arrives.
        var installation = await context.Installation.SingleAsync(cancellationToken);

        installation.IndexersSkipped = false;
        context.Installation.Update(installation);

        await context.SaveChangesAsync(cancellationToken);
        await discovery.InitialiseAsync(indexer.Id, check.Categories, cancellationToken);

        logger.LogInformation(
            "An indexer at {Host} has been added, searching {Count} of its categories.",
            HostOf(address),
            categories.Count);

        return new IndexerSave(IndexerConnectionOutcome.Saved, null, categories);
    }

    /// <summary>
    /// ADR 0020's indexer route: the same check, run again over a row that is
    /// already there. Null when there is no such row.
    /// </summary>
    /// <remarks>
    /// Nothing is written past a failure, exactly as when it was added — which
    /// is the point ADR 0020 makes about there being one form: the verification
    /// question is cheap here because the check was not rebuilt for it.
    /// </remarks>
    public async Task<IndexerSave?> EditAsync(
        Guid id,
        string? name,
        string? url,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var stored = await context.Indexers
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (stored is null)
        {
            return null;
        }

        var address = (url ?? string.Empty).Trim();

        // ADR 0002's identity again, minus this row: an address that is its own
        // is not a second one.
        if (await context.Indexers.AnyAsync(row => row.Url == address && row.Id != id, cancellationToken))
        {
            return new IndexerSave(IndexerConnectionOutcome.AlreadyAdded, null, []);
        }

        // ADR 0020: keys are write-only, so an empty field is the key that is
        // already on the row rather than no key.
        var submitted = (apiKey ?? string.Empty).Trim();
        var key = submitted.Length > 0 ? submitted : stored.ApiKey;

        var check = await newznab.CheckAsync(address, key, cancellationToken);

        if (check.Outcome is not IndexerConnectionOutcome.Saved)
        {
            return new IndexerSave(check.Outcome, check.Said, []);
        }

        var categories = IndexerConnection.MatchedByName(check.Categories);

        if (categories.Count == 0)
        {
            return new IndexerSave(IndexerConnectionOutcome.NoCategories, null, []);
        }

        stored.Name = string.IsNullOrWhiteSpace(name) ? HostOf(address) : name.Trim();
        stored.Url = address;
        stored.ApiKey = key;
        stored.Categories = string.Join(',', categories);
        stored.LastVerdict = IndexerConnectionOutcome.Saved;
        stored.LastCheckedAt = time.GetUtcNow();

        context.Indexers.Update(stored);
        await context.SaveChangesAsync(cancellationToken);
        await discovery.StoreCapsAsync(stored.Id, check.Categories, cancellationToken);

        logger.LogInformation(
            "The indexer at {Host} was checked again and its settings stored.",
            HostOf(address));

        return new IndexerSave(IndexerConnectionOutcome.Saved, null, categories);
    }

    /// <summary>
    /// The three settings that are the row's own rather than the connection's:
    /// whether it is used at all, and how much of it may be spent in a day.
    /// </summary>
    /// <remarks>
    /// Its own act (ADR 0040) and not a field on the edit request, for the
    /// reason ADR 0020 gives about spending queries: changing a budget is not a
    /// reason to run a real search against somebody's indexer, and an edit that
    /// re-checked would make disabling a broken indexer impossible — the check
    /// would fail and the change would be refused with it.
    /// </remarks>
    public async Task<IndexerSettingsSave?> SetAsync(
        Guid id,
        bool enabled,
        int? dailyQueryBudget,
        CancellationToken cancellationToken = default)
    {
        var stored = await context.Indexers.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (stored is null) return null;

        if (dailyQueryBudget is { } budget && (budget < 1 || budget > 100_000))
        {
            return new IndexerSettingsSave(false, stored.Enabled, stored.DailyQueryBudget);
        }

        stored.Enabled = enabled;

        // Empty is ADR 0020's unbounded, which the schema spells as a number
        // large enough that nothing reaches it — the column is not nullable and
        // making it so would put a second meaning on a value the budget
        // arithmetic divides.
        stored.DailyQueryBudget = dailyQueryBudget ?? Unbounded;

        context.Indexers.Update(stored);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "The indexer at {Host} is now {State}, with a daily query budget of {Budget}.",
            HostOf(stored.Url),
            enabled ? "enabled" : "disabled",
            stored.DailyQueryBudget);

        return new IndexerSettingsSave(true, stored.Enabled, stored.DailyQueryBudget);
    }

    /// <summary>
    /// One step up or down the list, which is ADR 0008's order and therefore
    /// ADR 0020's rank.
    /// </summary>
    /// <remarks>
    /// The whole list is renumbered from zero rather than two rows swapping
    /// numbers. Ranks that arrived by any other route — a Backup from an
    /// installation whose rows were deleted, a row added while another was
    /// mid-move — are then contiguous afterwards, and ADR 0008 only needs the
    /// order to be total.
    /// </remarks>
    public async Task<bool> MoveAsync(Guid id, bool up, CancellationToken cancellationToken = default)
    {
        var ordered = await context.Indexers
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Name)
            .ToListAsync(cancellationToken);

        var at = ordered.FindIndex(row => row.Id == id);

        if (at < 0) return false;

        var to = up ? at - 1 : at + 1;

        if (to < 0 || to >= ordered.Count) return true;

        (ordered[at], ordered[to]) = (ordered[to], ordered[at]);

        for (var position = 0; position < ordered.Count; position++)
        {
            ordered[position].Rank = position;
        }

        context.Indexers.UpdateRange(ordered);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// What deleting this indexer costs, before it is deleted.
    /// </summary>
    /// <remarks>
    /// Three different answers, which is why this is a sentence rather than a
    /// count. The cache goes with it and is disposable by ADR 0015. The
    /// Downloads stay, because the Download row is the consumed state ADR 0016
    /// keeps and the thing that answers <em>why is this on my disk</em>. And an
    /// Automation Rule that references it loses that permission — one left with
    /// none comes back <strong>disabled</strong> rather than inert, because
    /// ADR 0020 is explicit and the reason is ADR 0018's: a disabled rule shows
    /// as a Brake, and an inert one is a silent failure.
    /// </remarks>
    public async Task<IndexerDeletePreview?> PreviewDeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var stored = await context.Indexers
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (stored is null) return null;

        var cached = await context.Releases.CountAsync(row => row.IndexerId == id, cancellationToken);
        var downloads = await context.Downloads.CountAsync(row => row.IndexerId == id, cancellationToken);
        var referencing = await context.AutomationRuleIndexers
            .Where(row => row.IndexerId == id)
            .Select(row => row.AutomationRuleId)
            .ToListAsync(cancellationToken);
        var losingTheirLast = await context.AutomationRules
            .CountAsync(
                rule => referencing.Contains(rule.Id)
                    && !context.AutomationRuleIndexers.Any(
                        edge => edge.AutomationRuleId == rule.Id && edge.IndexerId != id),
                cancellationToken);

        return new IndexerDeletePreview(
            stored.Id,
            stored.Name,
            cached,
            downloads,
            referencing.Count,
            losingTheirLast);
    }

    /// <summary>Deletes it, and applies what the preview said.</summary>
    public async Task<IndexerDeleteVerdict?> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var stored = await context.Indexers.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (stored is null) return null;

        var preview = await PreviewDeleteAsync(id, cancellationToken);

        context.Indexers.Remove(stored);
        await context.SaveChangesAsync(cancellationToken);

        // After the delete, because the edges go with the row: a rule with no
        // permitted indexer left is one that can never act, and ADR 0020 wants
        // it saying so rather than quietly permitting nothing.
        var disabled = await context.AutomationRules
            .Where(rule => rule.Enabled
                && !context.AutomationRuleIndexers.Any(edge => edge.AutomationRuleId == rule.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(rule => rule.Enabled, false), cancellationToken);

        logger.LogWarning(
            "The indexer at {Host} was deleted. Its cache went with it; {Downloads} Download(s) keep "
            + "its identity, and {Disabled} Automation Rule(s) were disabled for having no Indexer left.",
            HostOf(stored.Url),
            preview?.Downloads ?? 0,
            disabled);

        return new IndexerDeleteVerdict(id, stored.Name, preview?.CachedReleases ?? 0, disabled);
    }

    /// <summary>
    /// ADR 0020's unbounded daily budget, as a number. Large enough that no
    /// indexer answers that many times in a day, and finite so that the budget
    /// arithmetic has no second case.
    /// </summary>
    public const int Unbounded = 100_000;

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var address) ? address.Host : url;
}

/// <param name="Saved">False where the budget was outside what a day can hold.</param>
public sealed record IndexerSettingsSave(bool Saved, bool Enabled, int DailyQueryBudget);

/// <param name="CachedReleases">Disposable by ADR 0015, and gone with the row.</param>
/// <param name="Downloads">
/// Kept: the Download row is what ADR 0016 makes the consumed state, and what
/// answers "why is this on my disk" after the indexer is gone.
/// </param>
/// <param name="RulesReferencing">Automation Rules that permit it today.</param>
/// <param name="RulesLosingTheirLastIndexer">
/// Of those, the ones that will have no permitted Indexer left and therefore
/// come back disabled (ADR 0020).
/// </param>
public sealed record IndexerDeletePreview(
    Guid IndexerId,
    string Name,
    int CachedReleases,
    int Downloads,
    int RulesReferencing,
    int RulesLosingTheirLastIndexer);

public sealed record IndexerDeleteVerdict(Guid IndexerId, string Name, int CachedReleases, int RulesDisabled);

/// <summary>
/// An indexer as the browser side sees it. No key: it is stored in the clear
/// (ADR 0037) and there is still no reason to hand it back out.
/// </summary>
public sealed record ConfiguredIndexer(
    Guid Id,
    string Name,
    string Url,
    string Categories,
    bool Enabled,
    int Rank,
    int DailyQueryBudget,
    IndexerConnectionOutcome LastVerdict,
    DateTimeOffset LastCheckedAt);

/// <summary>What happened to the indexer that was submitted.</summary>
/// <param name="Said">The indexer's own wording, when it refused in its own words.</param>
public sealed record IndexerSave(
    IndexerConnectionOutcome Outcome,
    string? Said,
    IReadOnlyList<string> Categories);
