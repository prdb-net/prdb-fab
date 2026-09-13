namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// A routine whose interval says how often to <em>look</em> rather than how
/// often to act, because what makes it due is a work set rather than a clock.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0032's distinction, written down where the schedule can read it.
/// <c>CONTEXT.md</c> already draws it in the definition of a Routine — paced by
/// a clock, or made due by a work set that is not empty — and until ADR 0061
/// every routine that spent a prdb request was the first kind.
/// </para>
/// <para>
/// The one thing that turns on it is <see cref="Sync.IdleProfile"/>, which is
/// what the schedule costs prdb <em>with nothing to do</em>. A routine of this
/// kind costs nothing then, by construction: with an empty work set it answers
/// <see cref="RunOutcome.NothingToDo"/> before it reaches the network. Counting
/// its cadence into the profile would mean an installation whose owner has never
/// opened a Preview being judged against requests it does not make — and,
/// worse, being shed for them.
/// </para>
/// <para>
/// It is not the opposite of <see cref="Sync.ISpendsPrdbBudget"/>. A routine can
/// be both, and ADR 0061's feed is: when its work set <em>is</em> not empty it
/// spends real requests on a real kind of work, and a plan too small should shed
/// it like anything else.
/// </para>
/// </remarks>
public interface IWorkSetPaced;
