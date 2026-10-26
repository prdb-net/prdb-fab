namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>A confirmed return path and its crash-recovery evidence, independent of mutable Library rows.</summary>
public sealed class CollectionUndoRow
{
    public Guid Id { get; set; }
    public Guid CollectionFileId { get; set; }
    public Guid VideoFileId { get; set; }
    public Guid VideoId { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string SourceRoot { get; set; } = string.Empty;
    public string DestinationRoot { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? OsHash { get; set; }
    public string? ContentHash { get; set; }
    public bool Approved { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? AttemptedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}
