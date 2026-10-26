namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>Local delivery deduplication; after Restore the current state is observed afresh.</summary>
public sealed class NotificationDeliveryRow
{
    public int Id { get; set; } = 1;
    public string? Fingerprint { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? Error { get; set; }
}
