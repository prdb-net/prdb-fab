using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// A durable desired prdb-account state which cannot be reconstructed from a
/// feed until its remote write has succeeded.
/// </summary>
public sealed class AccountPreferenceWriteRow
{
    public Guid Id { get; set; }
    public AccountPreferenceKind Kind { get; set; }
    public Guid EntityId { get; set; }
    public bool Desired { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? LastFailure { get; set; }
    public bool Blocked { get; set; }
}
