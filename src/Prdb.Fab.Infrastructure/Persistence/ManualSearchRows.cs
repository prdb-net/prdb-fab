using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>A person's recent, disposable request to search Indexers for one Video.</summary>
public sealed class ManualSearchRow
{
    public Guid Id { get; set; }
    public long VideoId { get; set; }
    public CatalogueVideoRow? Video { get; set; }
    public string Query { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
}

/// <summary>One selected Indexer's durable part of a Manual Search.</summary>
public sealed class ManualSearchIndexerRow
{
    public Guid SearchId { get; set; }
    public ManualSearchRow? Search { get; set; }
    public Guid IndexerId { get; set; }
    public IndexerRow? Indexer { get; set; }
    public ManualSearchIndexerState State { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? DeferredUntil { get; set; }
    public int ResultsSeen { get; set; }
    public int RowsAdded { get; set; }
    public string? Detail { get; set; }
}

/// <summary>A returned Release associated for explanation, never as Identification evidence.</summary>
public sealed class ManualSearchResultRow
{
    public Guid SearchId { get; set; }
    public ManualSearchRow? Search { get; set; }
    public long ReleaseId { get; set; }
    public ReleaseRow? Release { get; set; }
}
