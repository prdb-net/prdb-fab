using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Persistence;

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

        context.Indexers.Add(new IndexerRow
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
        });

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "An indexer at {Host} has been added, searching {Count} of its categories.",
            HostOf(address),
            categories.Count);

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
    IndexerConnectionOutcome LastVerdict,
    DateTimeOffset LastCheckedAt);

/// <summary>What happened to the indexer that was submitted.</summary>
/// <param name="Said">The indexer's own wording, when it refused in its own words.</param>
public sealed record IndexerSave(
    IndexerConnectionOutcome Outcome,
    string? Said,
    IReadOnlyList<string> Categories);
