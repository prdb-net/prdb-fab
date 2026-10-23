using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Core.Sync;

/// <summary>
/// How many prdb requests one repair pass may spend, from what the last
/// response said about the hourly window.
/// </summary>
/// <remarks>
/// The turn is bounded independently of quota admission. The Governor protects
/// the 20% account reserve and upcoming feed demand before each request, so this
/// estimate never grants permission to cross a shared account limit.
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
    public const int ABatch = CatalogueRead.ABatch;

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
