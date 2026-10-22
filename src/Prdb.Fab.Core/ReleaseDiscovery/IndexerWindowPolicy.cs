namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>Indexer policy is separate from the ninety-day prdb Catalogue scope.</summary>
public static class IndexerWindowPolicy
{
    public const int MinimumDays = 14;
    public const int InitialDays = 30;
    public const int MaximumDays = 90;
    public const int ResumeOverlap = 10;
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(1);
}
