using Prdb.Fab.Core.Reporting;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// The Fulfilment state prdb was last told, scoped to the account that was told.
/// Desired state remains a query over the Library and Wanted Videos.
/// </summary>
public sealed class ReportedStateRow
{
    public Guid VideoId { get; set; }

    public string UserHash { get; set; } = string.Empty;

    public bool IsFulfilled { get; set; }

    public FulfilmentQuality? Quality { get; set; }

    public DateTimeOffset? FulfilledAt { get; set; }

    public ReportingOutcome? TerminalOutcome { get; set; }
}
