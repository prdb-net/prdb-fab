namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The per-Indexer bound on the disposable Indexer Cache.</summary>
public static class IndexerCacheCeiling
{
    public const int Rows = 100_000;

    public static int OverBy(int held, int ceiling = Rows) => Math.Max(0, held - ceiling);
}
