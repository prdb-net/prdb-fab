namespace Prdb.Fab.Core.Access;

/// <summary>
/// ADR 0010: <em>sign-in is rate-limited, because one password with no username
/// is the easiest thing in the world to try repeatedly.</em>
/// </summary>
/// <remarks>
/// <para>
/// Counted for the installation rather than per caller, and that is the whole
/// decision. A per-address counter is the reflex, and here it protects against
/// nothing: the guessing worth stopping comes from somewhere with more than one
/// address, and it would sail past a limit that resets per address. There is
/// exactly one secret to guess, so the thing to ration is guesses at it.
/// </para>
/// <para>
/// The cost of that choice is real and is accepted: someone else's guessing
/// locks the owner out for the length of the window. Five minutes, and a
/// successful sign-in clears the count, so the owner who knows their password
/// waits once rather than repeatedly.
/// </para>
/// <para>
/// In memory, not in the database. A restart clearing the count is a restart at
/// the host, which is a bigger privilege than sign-in — and it keeps the
/// schedule's single writer out of a path that anyone unauthenticated can
/// reach.
/// </para>
/// </remarks>
public sealed class SignInThrottle(TimeProvider time)
{
    public const int AttemptsPerWindow = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private readonly Lock gate = new();

    private int attempts;
    private DateTimeOffset windowStartedAt;

    /// <summary>
    /// How long until the window resets, or null while attempts are still
    /// allowed. Asked before the password is checked, so that a locked-out
    /// caller never reaches the hash.
    /// </summary>
    public TimeSpan? RetryAfter()
    {
        lock (gate)
        {
            var now = time.GetUtcNow();

            if (now - windowStartedAt >= Window)
            {
                return null;
            }

            return attempts >= AttemptsPerWindow ? windowStartedAt + Window - now : null;
        }
    }

    public void RecordFailure()
    {
        lock (gate)
        {
            var now = time.GetUtcNow();

            if (now - windowStartedAt >= Window)
            {
                windowStartedAt = now;
                attempts = 0;
            }

            attempts++;
        }
    }

    /// <summary>Whoever knows the password is not who this is aimed at.</summary>
    public void RecordSuccess()
    {
        lock (gate)
        {
            attempts = 0;
            windowStartedAt = default;
        }
    }
}
