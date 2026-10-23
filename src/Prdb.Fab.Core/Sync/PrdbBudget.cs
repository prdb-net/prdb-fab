namespace Prdb.Fab.Core.Sync;

/// <summary>Observed shared account capacity in one sliding window.</summary>
/// <param name="Limit">The limit reported by prdb, never inferred from a package name.</param>
/// <param name="Remaining">Requests still available across all account clients.</param>
/// <param name="ResetIn">Time until the oldest request expires, not a full reset.</param>
public sealed record PrdbBudget(int Limit, int Remaining, TimeSpan ResetIn)
{
    public const double ReserveShare = 0.20;
    public static readonly TimeSpan ShortestWait = TimeSpan.FromMinutes(1);
    public bool Admits(PrdbWork work) => Remaining > ReserveFor(work);

    // Explicit key verification remains the recovery path. All unattended
    // work protects the same account reserve; background work additionally
    // protects recurring feed demand in the Governor.
    public int ReserveFor(PrdbWork work) => work is PrdbWork.Verification or PrdbWork.BudgetReading
        ? 0 : (int)Math.Ceiling(Limit * ReserveShare);

    public TimeSpan WaitBefore(PrdbWork work) =>
        Admits(work) ? TimeSpan.Zero : ResetIn > ShortestWait ? ResetIn : ShortestWait;
}
