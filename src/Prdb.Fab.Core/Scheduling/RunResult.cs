namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// What a routine hands back, and the one place ADR 0032's rule lives.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0038 left the shape of this to the skeleton, with one requirement: that
/// <em>an empty tick is not a run</em> be expressed once rather than said twice.
/// So it is expressed here, as an absence — <see cref="NothingToDo"/> is the
/// only result with no <see cref="Outcome"/>, and the store records exactly the
/// results that have one. A routine cannot forget the rule, because there is
/// nowhere to forget it.
/// </para>
/// <para>
/// ADR 0043's rule applies to this type as much as to anything: the three cases
/// are named rather than left to a null. A routine that returned
/// <c>RunResult?</c> would make <em>had nothing to do</em> and <em>never ran</em>
/// the same value, which is the collapse that ADR names as expensive.
/// </para>
/// </remarks>
public sealed record RunResult
{
    private RunResult(RunOutcome? outcome, int itemsHandled, string? reason, TimeSpan? dueIn = null)
    {
        Outcome = outcome;
        ItemsHandled = itemsHandled;
        Reason = reason;
        DueIn = dueIn;
    }

    /// <summary>
    /// What the run log records, or <see langword="null"/> when there is nothing
    /// to record because nothing ran.
    /// </summary>
    public RunOutcome? Outcome { get; }

    /// <summary>How much of its work set the run got through.</summary>
    public int ItemsHandled { get; }

    /// <summary>
    /// Why it failed, for a person reading the run log. Never read for control
    /// flow — ADR 0016 fixed that for SABnzbd's strings and the reason
    /// generalises: a sentence is for a reader.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// The work set was empty, so the routine was never due and nothing
    /// happened. ADR 0032: not a run, and therefore not recorded.
    /// </summary>
    public static RunResult NothingToDo { get; } = new(outcome: null, itemsHandled: 0, reason: null);

    /// <summary>The routine got through <paramref name="itemsHandled"/> of its work set.</summary>
    public static RunResult Handled(int itemsHandled) =>
        new(RunOutcome.Succeeded, itemsHandled, reason: null);

    /// <summary>The routine failed, with a sentence for whoever reads the log.</summary>
    /// <param name="waitFor">
    /// What the failure itself said about when to come back — prdb's
    /// <c>Retry-After</c>, which ADR 0014 has override the backoff exactly.
    /// Null when the failure said nothing, which is the ordinary case and the
    /// one backoff is for.
    /// </param>
    public static RunResult Failed(string reason, TimeSpan? waitFor = null) =>
        new(RunOutcome.Failed, itemsHandled: 0, reason, waitFor);

    /// <summary>
    /// The container was asked to stop while the routine was working. The count
    /// is what it had finished by then, which is the part worth keeping.
    /// </summary>
    public static RunResult Interrupted(int itemsHandled) =>
        new(RunOutcome.Interrupted, itemsHandled, reason: null);

    /// <summary>
    /// When the routine may next be due, where the run itself knows better than
    /// its cadence does — a deferral waiting on the budget, or a <c>429</c>
    /// carrying a <c>Retry-After</c> that ADR 0014 says overrides the backoff
    /// exactly. Null everywhere else, and the schedule falls back to what it
    /// knows.
    /// </summary>
    public TimeSpan? DueIn { get; }

    /// <summary>
    /// The governor turned the request away, so the routine did not run.
    /// </summary>
    /// <remarks>
    /// ADR 0014's fourth case, and it sits beside <see cref="NothingToDo"/>
    /// rather than beside <see cref="Failed"/> on purpose: a deferred routine is
    /// the tool working exactly as designed, which is the distinction ADR 0018
    /// later draws as a Brake against a Gap. So it moves no failure counter and
    /// writes no run — the same absence ADR 0032 gave the empty tick, and
    /// expressed in the same one place.
    /// </remarks>
    public static RunResult Deferred(TimeSpan waitFor) =>
        new(outcome: null, itemsHandled: 0, reason: null, dueIn: waitFor);

    /// <summary>Whether this belongs in the run log at all.</summary>
    public bool IsRecorded => Outcome is not null;
}
