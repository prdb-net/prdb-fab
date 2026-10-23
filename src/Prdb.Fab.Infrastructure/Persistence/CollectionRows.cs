namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>Durable first-collection barrier; absence preserves legacy automation.</summary>
public sealed class CollectionPreparationRow
{
    public int Id { get; set; } = 1;
    public string Phase { get; set; } = "Inventory";
    public long Revision { get; set; } = 1;
    public DateTimeOffset StartedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class CollectionRootRow
{
    public Guid Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public bool IsLibrary { get; set; }
}

/// <summary>Disposable enumeration position, rebuilt after Restore.</summary>
public sealed class CollectionWalkRow
{
    public string Path { get; set; } = string.Empty;
    public Guid RootId { get; set; }
    public string? AfterName { get; set; }
    public bool Completed { get; set; }
}

/// <summary>Scan provenance and approval for the Arriving File with the same id.</summary>
public sealed class CollectionFileRow
{
    public Guid Id { get; set; }
    public Guid RootId { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset LastWriteAt { get; set; }
    public bool Probed { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public string? PlannedPath { get; set; }
    public string? ApprovedEvidence { get; set; }
}

/// <summary>One completed Wanted baseline per account; never refilled.</summary>
public sealed class AutopilotBaselineRow
{
    public string UserHash { get; set; } = string.Empty;
    public DateTimeOffset BaselineAt { get; set; }
}

/// <summary>Both admitted and excluded intent, so replay cannot become new intent.</summary>
public sealed class AutopilotMembershipRow
{
    public string UserHash { get; set; } = string.Empty;
    public Guid VideoId { get; set; }
    public DateTimeOffset WantedSinceAt { get; set; }
    public bool Admitted { get; set; }
    public bool InitialBatch { get; set; }
}
