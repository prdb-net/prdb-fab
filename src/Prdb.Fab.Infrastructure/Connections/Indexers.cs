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
    public async Task<IReadOnlyList<ConfiguredIndexer>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Indexers
            .OrderBy(row => row.Name)
            .Select(row => new ConfiguredIndexer(
                row.Id,
                row.Name,
                row.Url,
                row.Categories,
                row.Enabled,
                row.Rank,
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

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var address) ? address.Host : url;
}

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
    IndexerConnectionOutcome LastVerdict,
    DateTimeOffset LastCheckedAt);

/// <summary>What happened to the indexer that was submitted.</summary>
/// <param name="Said">The indexer's own wording, when it refused in its own words.</param>
public sealed record IndexerSave(
    IndexerConnectionOutcome Outcome,
    string? Said,
    IReadOnlyList<string> Categories);
