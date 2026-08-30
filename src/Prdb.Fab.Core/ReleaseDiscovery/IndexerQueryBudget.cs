namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>Divides one Indexer's Daily Query Budget between its walk and Wanted Sweep.</summary>
public static class IndexerQueryBudget
{
    public const int SweepRequestsPerDay = 5 * 4 * 24;

    public static int ReservedForSweep(int dailyBudget) =>
        Math.Min(SweepRequestsPerDay, Math.Max(0, (dailyBudget + 1) / 2));

    public static bool Admits(
        int dailyBudget,
        int spent,
        int spentBySweep,
        IndexerQueryPurpose purpose,
        bool sweepHasWork = true)
    {
        if (dailyBudget <= 0 || spent >= dailyBudget)
        {
            return false;
        }

        var reserve = sweepHasWork ? ReservedForSweep(dailyBudget) : 0;

        return purpose switch
        {
            IndexerQueryPurpose.WantedSweep => spentBySweep < reserve,
            IndexerQueryPurpose.Walk or IndexerQueryPurpose.ManualSearch =>
                spent - spentBySweep < dailyBudget - reserve,
            _ => false,
        };
    }
}

public enum IndexerQueryPurpose
{
    Walk,
    WantedSweep,
    ManualSearch,
}
