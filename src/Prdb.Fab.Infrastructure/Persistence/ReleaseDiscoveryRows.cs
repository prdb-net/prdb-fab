using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Automation;

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
    public int SweepQueriesSpentToday { get; set; }
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
    /// <summary>
    /// The Newznab password attribute as reported. Only a present value other
    /// than <c>0</c> is a confession and excludes the Release (ADR 0008).
    /// </summary>
    public string? Password { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public IdentificationState IdentificationState { get; set; }
    public long? VideoId { get; set; }
    public CatalogueVideoRow? Video { get; set; }
    public IdentificationConfidence? Confidence { get; set; }
    public IdentificationRung? MatchedBy { get; set; }
    public long? SiteId { get; set; }
    public CatalogueSiteRow? Site { get; set; }
    public bool SearchWasReason { get; set; }
    /// <summary>Whether this Release belongs to the bounded Decide work set.</summary>
    public bool AutomationPending { get; set; }
    /// <summary>The latest deliberate automatic non-act, kept for the Release view.</summary>
    public AutomationDecisionReason? AutomationDecisionReason { get; set; }
    public DateTimeOffset? AutomationDecisionAt { get; set; }
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
