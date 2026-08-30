namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public static class DiscoveryRoutineNames
{
    public const string Caps = "indexer.caps";
    public const string Walk = "indexer.walk";
    public const string Bootstrap = "indexer.walk.bootstrap";
    public const string CatchUp = "indexer.walk.catch-up";
    public const string WantedSweep = "indexer.wanted-sweep";
    public const string ManualSearch = "indexer.manual-search";
    public const string ManualSearchRetention = "release.manual-search-retention";
    public const string Screening = "release.screening";
    public const string BackwardsSearch = "release.backwards-search";
    public const string Identification = "release.identification";
}
