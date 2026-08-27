namespace Prdb.Fab.Core.Scheduling;

/// <summary>
/// How long a routine waits after failing. ADR 0014: the routine's own interval
/// doubled per failure, capped at an hour, reset on success.
/// </summary>
/// <remarks>
/// A separate mechanism from the Gap, and the distinction is worth keeping in
/// sight: backoff is how often a broken thing is tried, and the Gap is whether
/// anybody is told. Three consecutive failures raise one; backoff has been
/// slowing the retries down since the first.
/// </remarks>
public static class Backoff
{
    /// <summary>ADR 0014's cap. Beyond an hour a routine has stopped rather than slowed.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

    /// <summary>
    /// How long after <paramref name="consecutiveFailures"/> failures a routine
    /// with this <paramref name="cadence"/> may next be due.
    /// </summary>
    public static TimeSpan After(TimeSpan cadence, int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return cadence;
        }

        // Doubled per failure, and the shift is done on the multiplier rather
        // than on the ticks so that a long cadence cannot overflow its way
        // round to a routine that is due immediately.
        var doublings = Math.Min(consecutiveFailures, 32);
        var multiplier = 1L << doublings;

        return cadence.Ticks > Longest.Ticks / multiplier
            ? Longest
            : cadence * multiplier;
    }
}
