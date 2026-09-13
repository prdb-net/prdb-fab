namespace Prdb.Fab.Core.Sync;

/// <summary>
/// What prdb said about the hourly window on the last response, and the rule
/// that reads it: whether a request of a given kind may be sent now.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0014's governor, as a value. The numeric limit is in no prdb document —
/// it arrives on the responses the tool is already getting — so this cannot be
/// a cadence and cannot be a setting: a control whose correct value is a number
/// nobody can see is ADR 0020's control with no correct value.
/// </para>
/// <para>
/// The rule is a reserve per kind of work, as a share of the limit rather than
/// as a count, because the limit differs per plan and the point is the
/// <em>order</em> things are given up in. Only one of the shares is a number
/// ADR 0014 states: repair may spend whatever holds hourly usage under half of
/// the limit, which is the reserve of one half below. The rest are a staircase
/// under it, in ADR 0014's order, spaced widely enough that a small plan still
/// separates them.
/// </para>
/// </remarks>
/// <param name="Limit">How many requests the hour allows in total.</param>
/// <param name="Remaining">How many of them are left.</param>
/// <param name="ResetIn">
/// How long until the oldest request leaves the sliding window and frees one
/// slot. Not the time until the window empties, which prdb does not report.
/// </param>
public sealed record PrdbBudget(int Limit, int Remaining, TimeSpan ResetIn)
{
    /// <summary>
    /// How long a deferred request waits before its routine is asked to try
    /// again. Never shorter than this, so that a lane refused at the bottom of
    /// the staircase does not come back every tick to be refused again.
    /// </summary>
    public static readonly TimeSpan ShortestWait = TimeSpan.FromMinutes(1);

    /// <summary>Whether a request for <paramref name="work"/> may be sent now.</summary>
    public bool Admits(PrdbWork work) => Remaining > ReserveFor(work);

    /// <summary>
    /// How many requests are held back from <paramref name="work"/> for the
    /// kinds above it.
    /// </summary>
    public int ReserveFor(PrdbWork work) => (int)Math.Ceiling(Limit * ShareHeldBack(work));

    /// <summary>
    /// How long to wait before asking again. One slot frees at
    /// <see cref="ResetIn"/>, which is the soonest anything can change.
    /// </summary>
    public TimeSpan WaitBefore(PrdbWork work) =>
        Admits(work) ? TimeSpan.Zero : ResetIn > ShortestWait ? ResetIn : ShortestWait;

    private static double ShareHeldBack(PrdbWork work) => work switch
    {
        // Neither can be held back at all: one is a person waiting, the other
        // is a file that has arrived and cannot be filed until prdb answers.
        PrdbWork.Verification or PrdbWork.Identification => 0,

        PrdbWork.Writes => 0.05,

        // Not a step on the five-point staircase below, because the staircase
        // was laid out before this existed: moving every number on it to keep
        // the steps round would change what each feed is held back from, for
        // nothing. Between a write and a feed is where this belongs, and eight
        // is between.
        PrdbWork.Preview => 0.08,

        PrdbWork.UserFeeds => 0.10,
        PrdbWork.WhatsNew => 0.15,
        PrdbWork.Images => 0.20,
        PrdbWork.Actors => 0.25,
        PrdbWork.Sites => 0.30,

        // ADR 0061, and off the staircase for the same reason Preview is: the
        // steps were laid out before this existed. Below every feed, because
        // nothing waits on it and the Catalogue's own currency is worth more
        // than a picture being an hour out of date; above repair, which spends
        // only what is left above half.
        PrdbWork.UserPreviews => 0.35,

        // ADR 0014's own number, and the only one here that is not a step on
        // the staircase: repair runs on what is left above half the limit.
        PrdbWork.Repair => 0.50,

        _ => throw new ArgumentOutOfRangeException(
            nameof(work),
            work,
            "Every kind of prdb request has a place in ADR 0014's order of precedence."),
    };
}
