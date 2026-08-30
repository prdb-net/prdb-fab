namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// The resumable prdb half of the Recent Window. There is one source and
/// therefore one row; Indexer progress belongs to each Indexer's walk state.
/// </summary>
public sealed class RecentWindowStateRow
{
    public const int TheOnlyRow = 1;

    public int Id { get; set; } = TheOnlyRow;
    public int CatalogueResumePage { get; set; } = 1;
    public DateTimeOffset? CataloguePassStartedAt { get; set; }
    public DateTimeOffset? CatalogueCompletedAt { get; set; }
    public DateTimeOffset? CatalogueOldestCreatedAt { get; set; }
}
