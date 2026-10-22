namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>A measured, resumable coverage proof for one Indexer range.</summary>
public sealed class IndexerWindowRow
{
    public Guid IndexerId { get; set; }
    public IndexerRow? Indexer { get; set; }
    public int Days { get; set; }
    public int ResumeOffset { get; set; }
    public string? AnchorReleaseId { get; set; }
    public DateTimeOffset? PassStartedAt { get; set; }
    public DateTimeOffset? ProofStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? OldestPostDate { get; set; }
    public int PassQueries { get; set; }
    public int LastPassQueries { get; set; }
}
