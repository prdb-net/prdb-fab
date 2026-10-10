namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>Last observed account restriction, including its removal stamp.</summary>
public sealed class IgnoredSiteRow
{
    public Guid SitePrdbId { get; set; }
    public bool Ignored { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
