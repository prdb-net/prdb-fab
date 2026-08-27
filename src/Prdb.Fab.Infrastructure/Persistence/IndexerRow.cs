using Prdb.Fab.Core.Connections;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// ADR 0033's <c>Indexer</c>: one configured Newznab-style search service,
/// exported, and one of the two rows an indexer is.
/// </summary>
/// <remarks>
/// <para>
/// The other one is <c>IndexerWalkState</c> — the watermark, the resume page,
/// the stored capabilities tree and the queries spent today. ADR 0033 split them
/// because the export boundary runs between tables and never through one: this
/// half is configuration somebody typed and cannot type again, and that half is
/// cache that refills itself.
/// </para>
/// <para>
/// Only the columns this slice reads or writes, which is ticket 01's rule
/// applied where that ticket said it would be: enabled, rank and the daily query
/// budget belong to the walk and to ADR 0020's indexer route, and arrive with
/// them.
/// </para>
/// </remarks>
public sealed class IndexerRow
{
    /// <summary>ADR 0033: an exported table gets a UUIDv7.</summary>
    public Guid Id { get; set; }

    /// <summary>What the user calls it. Theirs, not the server's.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The API address, as typed.
    /// </summary>
    /// <remarks>
    /// The whole address rather than a host with a path assumed, because the
    /// research found the path is not a constant: the convention is <c>/api</c>,
    /// and one of the implementations surveyed serves it at
    /// <c>/api/v1/api</c> — which is why one of the clients ships that as a
    /// per-indexer setting.
    /// </remarks>
    public string Url { get; set; } = string.Empty;

    /// <summary>ADR 0037: stored as it was typed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The categories this tool searches here, by name and comma-separated —
    /// ADR 0033's <em>category names matched by name</em>.
    /// </summary>
    /// <remarks>
    /// Names rather than numbers because ADR 0002's numbers are the indexer's
    /// own and the research found them contradicting each other across
    /// implementations: <c>6070</c> is <em>Packs</em> in two of them and
    /// <em>Other</em> in a third. The numbers are re-read from the capabilities
    /// document whenever the walk needs them, and a backup restored against an
    /// indexer that has renumbered its tree still says what was meant.
    /// </remarks>
    public string Categories { get; set; } = string.Empty;

    /// <summary>
    /// The verdict of the last check against it. <c>CONTEXT.md</c> puts this on
    /// a Connection, and ADR 0033 reserves the word for exactly this use.
    /// Nothing reads it yet — the Status page is a slice of its own.
    /// </summary>
    public IndexerConnectionOutcome LastVerdict { get; set; }

    public DateTimeOffset LastCheckedAt { get; set; }
}
