using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Scheduling;

/// <summary>
/// Whether the prdb plan carries the schedule, and what the schedule does when
/// it does not.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014's named condition. The governor answers a <em>momentary</em>
/// shortage by holding one request back in the order of precedence; that is a
/// Brake, and a tool that only had it would defer everything forever under a
/// plan too small while nothing ever failed. This answers the
/// <em>permanent</em> shortage: less is asked for, in a fixed documented order,
/// and the fact that it was necessary is recorded.
/// </para>
/// <para>
/// <strong>It reads the limit rather than holding one.</strong> The number is
/// on prdb's own responses (<see cref="PrdbGovernor.LastReading"/>), so raising
/// a plan lifts the shedding on the next answer that arrives — no restart, and
/// nothing to tell the tool. That is also why nothing here is a setting:
/// ADR 0020 admits a control where the tool cannot know the answer, and this
/// answer arrives on every metered response.
/// </para>
/// </remarks>
public sealed class ThePlan(
    FabDbContext context,
    PrdbGovernor governor,
    TimeProvider time,
    ILogger<ThePlan> logger)
{
    /// <summary>
    /// What was last written down about the plan, so that a reading that has
    /// aged out does not undo it.
    /// </summary>
    /// <remarks>
    /// Read by <see cref="NoteAsync"/> at the start of a turn and used for the
    /// rest of it. The class is scoped, so this lives exactly one turn of one
    /// lane, which is as long as the answer can be relied on anyway.
    /// </remarks>
    private bool recorded;

    /// <summary>
    /// Whether load is being shed, from the discovered limit where there is one
    /// and from what was recorded where there is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback is the whole of the reason this is not simply a question
    /// about the last reading. prdb's hour is a sliding window, so a reading
    /// older than an hour says nothing and the governor stops offering it —
    /// and under a plan too small the schedule is <em>already</em> slowed to the
    /// point where an hour can pass between requests. Reading the absence as
    /// <em>carried</em> would restore the full cadence, spend the request that
    /// discovers the limit again, and shed again: a condition that flickers, on
    /// a stamp whose only use is saying how long it has been true.
    /// </para>
    /// <para>
    /// A fresh installation that has asked nothing has neither a reading nor a
    /// record, and is not shed. That is the same rule read from the other end:
    /// nothing is claimed until prdb has said something.
    /// </para>
    /// </remarks>
    public bool Shedding =>
        governor.LastReading is { } reading ? !IdleProfile.CarriedBy(reading.Limit) : recorded;

    /// <summary>Whether the plan is known not to carry <see cref="IdleProfile"/>.</summary>
    public bool CarriesTheSchedule => !Shedding;

    /// <summary>
    /// How long after this run <paramref name="routine"/> may next be due.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The routine's own cadence, unless it spends prdb requests on a clock and
    /// the plan cannot carry the profile — in which case it is what ADR 0014
    /// sheds that kind of work to. A routine that spends nothing at prdb, and a
    /// one-shot bootstrap that is trying to finish and retire, are untouched:
    /// shedding a bootstrap to a day would not be a degradation but a stop.
    /// </para>
    /// <para>
    /// Read here rather than written to the row, because the row keeps what is
    /// genuinely its own (the registrar says so) and a stored cadence would be a
    /// second copy of a number that changes when a plan does.
    /// </para>
    /// </remarks>
    public TimeSpan CadenceFor(IRoutine routine) =>
        routine is ISpendsPrdbBudget spender and not IOneShot
            ? IdleProfile.CadenceFor(spender.Spends, routine.Cadence, Shedding)
            : routine.Cadence;

    /// <summary>
    /// Records the condition, or clears it, and does neither where nothing has
    /// changed.
    /// </summary>
    /// <remarks>
    /// Once rather than every run, which is the whole of why this compares
    /// before it writes: a Gap that is re-recorded every tick is a log nobody
    /// can read and a stamp that always says <em>now</em>, when the one thing
    /// worth knowing about this condition is how long it has been true.
    /// </remarks>
    public async Task NoteAsync(CancellationToken cancellationToken)
    {
        var since = await context.Installation
            .Select(row => row.PlanShortSince)
            .SingleOrDefaultAsync(cancellationToken);

        recorded = since is not null;

        if (governor.LastReading is not { } reading)
        {
            // Nothing has been read, or what was read has aged out of the hour
            // it described. Neither is evidence about the plan, so nothing is
            // written and what stands stays standing.
            return;
        }

        var carried = IdleProfile.CarriedBy(reading.Limit);

        if (carried == !recorded)
        {
            return;
        }

        recorded = !carried;

        if (carried)
        {
            logger.LogInformation(
                "The prdb plan carries the schedule again. Nothing is being shed.");

            await WriteAsync(null, cancellationToken);
            return;
        }

        // ADR 0043: the sentence says what was given up as well as what
        // happened, because the person reading it is being told their tool is
        // deliberately slower than it was designed to be.
        logger.LogWarning(
            "The prdb plan allows {Limit} requests an hour, which does not carry the schedule's "
            + "{Profile:0.#} an hour. The actors feed drops to daily, the images feed and What's New "
            + "to hourly, and the repair pass to its minimum.",
            governor.LastReading?.Limit,
            IdleProfile.RequestsAnHour);

        await WriteAsync(time.GetUtcNow(), cancellationToken);
    }

    private Task WriteAsync(DateTimeOffset? since, CancellationToken cancellationToken) =>
        context.Installation.ExecuteUpdateAsync(
            row => row.SetProperty(installation => installation.PlanShortSince, since),
            cancellationToken);
}
