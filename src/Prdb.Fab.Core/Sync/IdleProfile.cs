namespace Prdb.Fab.Core.Sync;

/// <summary>
/// What the schedule costs prdb when nothing is happening, and what is given up
/// when a plan cannot carry it.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014's named condition, and the reason the governor is not enough on its
/// own: <em>if the discovered limit cannot carry the idle profile, load is shed
/// in a fixed documented order and a Gap says the plan does not carry the
/// schedule.</em> Without it the governor would defer everything forever while
/// nothing ever failed — the silent-failure shape four ADRs each contributed one
/// of to the status page.
/// </para>
/// <para>
/// <strong>Shedding is not backoff and not a deferral.</strong> The order of
/// precedence (<see cref="PrdbBudget"/>) answers a momentary shortage by holding
/// one request back; this answers a permanent one by asking for less
/// altogether. The two must not be mistakable for each other, which is why one
/// writes a deferral on a routine row and this changes what the row is due
/// again in.
/// </para>
/// <para>
/// <strong>None of it is a setting.</strong> ADR 0014 refused intervals so that
/// a user cannot break their own rate limit and then report it as a bug, and
/// ADR 0020's admission rule admits none of these numbers: the limit is
/// discovered rather than chosen, and what to give up first is a judgement about
/// this tool rather than about this installation.
/// </para>
/// </remarks>
public static class IdleProfile
{
    /// <summary>
    /// How many prdb requests an hour the schedule makes with nothing to do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// About nine, which is ADR 0014's table added up: What's New four times an
    /// hour, the images feed twice, the wanted list and the two favourites
    /// feeds once each, the actors feed every six hours and the site list every
    /// twenty-four. A test adds the cadences of the routines actually
    /// registered and fails the build if they stop agreeing with this, which is
    /// what keeps a routine added later from quietly moving the number every
    /// later routine is judged against.
    /// </para>
    /// <para>
    /// One request per run is the assumption, and it is the same one ADR 0014's
    /// table makes. A run that pages spends more, but paging happens when there
    /// is something to fetch — and this is the figure for a schedule with
    /// nothing to do. The repair pass is not in it at all: ADR 0013 steers it by
    /// what is left of the budget rather than by a cadence, so it consumes the
    /// slack rather than adding to the demand.
    /// </para>
    /// </remarks>
    public const double RequestsAnHour = 4 + 2 + 1 + 1 + 1 + (1.0 / 6) + (1.0 / 24);

    /// <summary>
    /// The share of an hourly limit the recurring schedule may occupy.
    /// </summary>
    /// <remarks>
    /// A half, and it is not a new number: ADR 0014 gives the repair pass
    /// <em>whatever holds hourly usage under half of the limit</em>, which
    /// makes the half the line all background work stays under. The other half
    /// is what a person or an arrived file is waiting on — verification,
    /// identification, writes — and none of those can be shed, so none of them
    /// may be crowded out by a feed that runs on a clock.
    /// </remarks>
    public const double ShareOfTheLimit = 0.5;

    /// <summary>
    /// Whether a plan of this hourly limit carries the schedule.
    /// </summary>
    /// <param name="limit">
    /// The limit read off prdb's own headers, or null where nothing has been
    /// read yet — which is not the same as a plan that is too small, and is
    /// answered as <em>carried</em> so that an installation that has asked
    /// nothing yet does not start out degraded.
    /// </param>
    public static bool CarriedBy(int? limit) =>
        limit is not { } hourly || hourly * ShareOfTheLimit >= RequestsAnHour;

    /// <summary>
    /// What a kind of work is given up to, or null where it is not shed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR 0014's order, quoted: <em>actors to 24 h, images and What's New to
    /// 60 min, repair to its minimum</em>. The last of those needs nothing
    /// here — <see cref="RepairBudget"/> already floors the pass at one request
    /// a run, which is its minimum — so what is left is the three cadences.
    /// </para>
    /// <para>
    /// The user feeds are not shed, and that is the point of the order rather
    /// than an omission: the wanted list is ADR 0007's only source of intent,
    /// so a tool that stops reading it stops being able to want anything. What
    /// is given up is knowing about videos nobody has asked for.
    /// </para>
    /// <para>
    /// It is a fixed table and not a scheduler reasoning about itself. A
    /// degradation the user can be told about has to be one sentence long, and
    /// <em>the plan does not carry the schedule, so the actors feed now runs
    /// daily</em> is that sentence.
    /// </para>
    /// </remarks>
    public static TimeSpan? ShedCadenceFor(PrdbWork work) => work switch
    {
        PrdbWork.Actors => TimeSpan.FromHours(24),
        PrdbWork.Images or PrdbWork.WhatsNew => TimeSpan.FromMinutes(60),
        _ => null,
    };

    /// <summary>
    /// The cadence a routine spending <paramref name="work"/> actually runs at.
    /// </summary>
    /// <param name="shedding">
    /// Whether the plan has been found not to carry the schedule. A judgement
    /// rather than the limit itself, because it outlives any one reading — see
    /// <c>ThePlan</c>, where a discovered limit and a recorded condition are
    /// reconciled into it.
    /// </param>
    /// <remarks>
    /// Never faster than the routine asked for: a shed cadence that came out
    /// shorter than the one in the code would be this table quietly speeding
    /// something up, which is not a degradation and not what any of it is for.
    /// </remarks>
    public static TimeSpan CadenceFor(PrdbWork work, TimeSpan cadence, bool shedding) =>
        !shedding || ShedCadenceFor(work) is not { } shed || shed < cadence
            ? cadence
            : shed;
}
