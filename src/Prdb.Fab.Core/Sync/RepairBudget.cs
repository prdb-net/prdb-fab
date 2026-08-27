using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Core.Sync;

/// <summary>
/// How many prdb requests one repair pass may spend, from what the last
/// response said about the hourly window.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0013 steers the repair pass by a request budget rather than by a
/// cadence, because the numeric value of the rate limit is in no prdb document
/// and only arrives on the responses the tool is already getting — so a budget
/// is the only control that can be sized against a limit discovered while
/// running. ADR 0014 gives that budget its number: whatever holds hourly usage
/// under half of the discovered limit, which is
/// <see cref="PrdbBudget.ReserveFor"/> for <see cref="PrdbWork.Repair"/>.
/// </para>
/// <para>
/// The governor already refuses a repair request that would cross that line, so
/// what this adds is the other half of ADR 0014's sentence: <strong>at least
/// one request per run</strong>. Repair is last in the order of precedence, and
/// arithmetic that rounded its allowance down to zero on a small plan would
/// stall it forever without anything failing — which is the silent shape
/// ADR 0018 is built to make visible and is cheaper not to create.
/// </para>
/// </remarks>
public static class RepairBudget
{
    /// <summary>
    /// What a run spends however short the budget is. The governor may still
    /// defer it, and that is a Brake rather than a Gap; what it may not do is
    /// be talked out of asking.
    /// </summary>
    public const int AtLeast = 1;

    /// <summary>
    /// The most requests one run makes, whatever the plan allows.
    /// </summary>
    /// <remarks>
    /// Ten, which is five hundred videos re-read in one turn of the bulk lane.
    /// The bound is not the budget — a large plan has room for far more — it is
    /// ADR 0032's rule that a run is bounded and yields: repair comes round
    /// again at its idle tick, so a generous limit is spent over many short
    /// runs rather than in one that holds the lane for minutes.
    /// </remarks>
    public const int MostPerRun = 10;

    /// <summary>How many videos one request reads back.</summary>
    public const int ABatch = Backfill.ABatch;

    /// <summary>
    /// What this run may spend, given what prdb last said — or
    /// <see cref="AtLeast"/> where it has said nothing yet, because the request
    /// is how the first reading arrives.
    /// </summary>
    public static int RequestsFor(PrdbBudget? budget) =>
        budget is null
            ? AtLeast
            : Math.Clamp(budget.Remaining - budget.ReserveFor(PrdbWork.Repair), AtLeast, MostPerRun);

    /// <summary>How many videos those requests cover.</summary>
    public static int VideosFor(PrdbBudget? budget) => RequestsFor(budget) * ABatch;
}
