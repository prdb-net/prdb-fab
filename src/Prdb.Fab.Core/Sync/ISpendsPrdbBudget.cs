namespace Prdb.Fab.Core.Sync;

/// <summary>
/// A routine that spends prdb requests on a clock, and says which kind of work
/// it spends them on.
/// </summary>
/// <remarks>
/// <para>
/// Two things read this, and neither of them could be written without it. The
/// schedule reads it to shed load in ADR 0014's order, which is expressed per
/// kind of work rather than per routine — five feed routines share one
/// implementation, so a table of routine names would have had to name the same
/// thing twice. And a test reads it to add up <see cref="IdleProfile"/> over
/// the routines actually registered, which is what makes the profile an
/// assertion rather than a comment.
/// </para>
/// <para>
/// <strong>Only routines paced by a clock.</strong> The repair pass is steered
/// by what is left of the budget rather than by a cadence (ADR 0013), the
/// one-shot bootstraps run once and retire (ADR 0014), and the artwork routine
/// spends no prdb request at all (ADR 0030) — so none of the three is one of
/// these, and the idle profile is exactly the routines that are.
/// </para>
/// </remarks>
public interface ISpendsPrdbBudget
{
    /// <summary>Which of ADR 0014's kinds of work this routine's requests are.</summary>
    PrdbWork Spends { get; }
}
