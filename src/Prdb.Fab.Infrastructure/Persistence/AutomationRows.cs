namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>ADR 0007's exported, unordered permission over Wanted Videos.</summary>
public sealed class AutomationRuleRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public long? MinimumSize { get; set; }
    public long? MaximumSize { get; set; }
}

/// <summary>The allowed-Indexer edge of one Automation Rule.</summary>
public sealed class AutomationRuleIndexerRow
{
    public Guid AutomationRuleId { get; set; }
    public AutomationRuleRow? AutomationRule { get; set; }
    public Guid IndexerId { get; set; }
    public IndexerRow? Indexer { get; set; }
}
