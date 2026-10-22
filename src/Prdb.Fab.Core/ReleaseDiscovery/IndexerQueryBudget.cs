namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>Protected remaining demand, recalculated from local measurements.</summary>
public sealed record IndexerBudgetPlan(int DailyBudget, int Spent, int HeadReserve,
    int MinimumReserve, int SearchReserve)
{
    public int MinimumDailyCost { get; init; }
    public int Remaining => Math.Max(0, DailyBudget - Spent);
    public int RequiredShortfall => Math.Max(0, HeadReserve + MinimumReserve - Remaining);
    public int AvailableFor(IndexerQueryPurpose purpose) => Math.Max(0, Remaining - (purpose switch
    {
        // A tiny plan still discovers new Releases rather than reserving every
        // request for work it cannot complete. The shortfall stays visible.
        IndexerQueryPurpose.Walk => 0,
        IndexerQueryPurpose.MinimumWindow => HeadReserve,
        IndexerQueryPurpose.WantedSweep or IndexerQueryPurpose.ManualSearch => HeadReserve + MinimumReserve,
        IndexerQueryPurpose.WorkingWindow or IndexerQueryPurpose.OptionalHistory => HeadReserve + MinimumReserve + SearchReserve,
        _ => Remaining,
    }));
}

public static class IndexerQueryBudget
{
    public const int PollsPerDay = 24 * 4;
    public const int SweepRequestsPerDay = 5 * PollsPerDay;
    public static IndexerBudgetPlan Plan(int dailyBudget, int spent, DateTimeOffset now,
        int measuredHeadPages, int minimumRemaining, int searchableWanted, int manualRemaining)
    {
        var tomorrow = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var polls = Math.Max(0, (int)Math.Ceiling((tomorrow - now).TotalMinutes / 15));
        var head = polls * Math.Clamp(measuredHeadPages, 1, 10);
        var search = Math.Min(SweepRequestsPerDay, Math.Min(5, searchableWanted) * polls) + Math.Max(0, manualRemaining);
        return new(Math.Max(0, dailyBudget), Math.Max(0, spent), head,
            Math.Max(0, minimumRemaining), Math.Min(Math.Max(0, dailyBudget - head - minimumRemaining), search));
    }
}

public enum IndexerQueryPurpose
{
    Walk,
    WantedSweep,
    ManualSearch,
    MinimumWindow,
    WorkingWindow,
    OptionalHistory,
}
