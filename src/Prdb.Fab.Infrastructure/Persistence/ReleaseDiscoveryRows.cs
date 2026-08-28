using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Persistence;

public sealed class IndexerWalkStateRow
{
    public Guid IndexerId { get; set; }
    public IndexerRow? Indexer { get; set; }
    public DateTimeOffset? WatermarkPostDate { get; set; }
    public string? WatermarkReleaseId { get; set; }
    public string CapsTree { get; set; } = "[]";
    public string ResolvedCategoryIds { get; set; } = "[]";
    public string MissingCategoryNames { get; set; } = "[]";
    public DateTimeOffset? CapsCheckedAt { get; set; }
    public DateTimeOffset QueryDay { get; set; }
    public int QueriesSpentToday { get; set; }
    public int? ResumePage { get; set; }
    public DateTimeOffset? BootstrapCompletedAt { get; set; }
    public DateTimeOffset? CatchUpFrom { get; set; }
    public DateTimeOffset? CatchUpTo { get; set; }
    public string? CatchUpCause { get; set; }
}

public sealed class ReleaseRow
{
    public long Id { get; set; }
    public Guid IndexerId { get; set; }
    public IndexerRow? Indexer { get; set; }
    public string DerivedReleaseId { get; set; } = string.Empty;
    public string RawGuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string NormalisedTitle { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string Categories { get; set; } = "[]";
    public DateTimeOffset PostDate { get; set; }
    public DateTimeOffset PubDate { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public IdentificationState IdentificationState { get; set; }
    public long? VideoId { get; set; }
    public decimal? Confidence { get; set; }
    public string? MatchedBy { get; set; }
    public long? SiteId { get; set; }
    public bool SearchWasReason { get; set; }
}

public sealed class ReleaseCandidateRow
{
    public long ReleaseId { get; set; }
    public ReleaseRow? Release { get; set; }
    public long VideoId { get; set; }
    public CatalogueVideoRow? Video { get; set; }
}

public sealed class WantedVideoSweepStateRow
{
    public long VideoId { get; set; }
    public WantedVideoRow? Video { get; set; }
    public Guid IndexerId { get; set; }
    public IndexerRow? Indexer { get; set; }
    public DateTimeOffset? LastSearchedAt { get; set; }
}

public sealed class IdentificationOutcomeRow
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Gate { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
}
