namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// ADR 0014: a restart spreads its overdue routines rather than firing them all
/// at once, across the smaller of their own interval and five minutes.
/// </summary>
/// <remarks>
/// What it is for is the moment after an update, when every routine in the
/// table has been overdue for as long as the container was down. Without the
/// spread that arrives at prdb and at every indexer as one burst — the shape a
/// rate limit is least forgiving of, and the one a person reading a log would
/// have the hardest time recognising as self-inflicted.
/// </remarks>
public static class RestartSpread
{
    /// <summary>ADR 0014's ceiling on the window.</summary>
    public static readonly TimeSpan Widest = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The live lane is exempt and starts immediately: a download in flight has
    /// to be picked up at once, and nothing in that lane leaves this container.
    /// </summary>
    public static bool Exempts(Lane lane) => lane == Lane.Live;

    /// <summary>
    /// How long after the restart the routine at <paramref name="position"/> of
    /// <paramref name="count"/> overdue ones may be due.
    /// </summary>
    /// <remarks>
    /// The first is due at once, so a restart is never slower than no spread at
    /// all for whatever comes first. The rest are spaced evenly across the
    /// window, which makes the spread deterministic — the same table restarted
    /// twice fires in the same order at the same offsets, and there is nothing
    /// random to reproduce when somebody asks why a routine ran when it did.
    /// </remarks>
    public static TimeSpan OffsetFor(int position, int count, TimeSpan cadence)
    {
        if (position <= 0 || count <= 1)
        {
            return TimeSpan.Zero;
        }

        var window = cadence < Widest ? cadence : Widest;

        return window * position / count;
    }
}
