using System.Globalization;

using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// ADR 0014's governor: what decides whether a prdb request is sent now or
/// deferred, from the rate limit read off the last response rather than from a
/// number known in advance.
/// </summary>
/// <remarks>
/// <para>
/// One instance for the process, because what it holds is one account's hourly
/// window and the window is shared by every routine that spends from it. It is
/// asked by the handler on the prdb transport (ADR 0041) rather than by each
/// caller, which is what makes <em>every request passes it</em> true rather
/// than conventional: a call site that forgot to ask would still be asked for.
/// </para>
/// <para>
/// It reads the three hourly headers itself. The SDK offers the same numbers
/// typed, as a per-request option, and that is the better shape for a caller
/// who wants them — but it is per caller, and a governor that only sees the
/// requests whose authors remembered to hand it an option is not a governor.
/// </para>
/// </remarks>
public sealed class PrdbGovernor(TimeProvider time, ILogger<PrdbGovernor> logger)
{
    /// <summary>
    /// A reading older than the window it describes says nothing: prdb's hour
    /// is a sliding window, so an hour after the reading every request in it
    /// has left.
    /// </summary>
    private static readonly TimeSpan ReadingKeeps = TimeSpan.FromHours(1);

    /// <summary>
    /// What the routine currently on this lane is doing, so that the handler
    /// knows what it is being asked to send.
    /// </summary>
    /// <remarks>
    /// Ambient rather than a parameter because the request is built by the SDK,
    /// several layers below whoever knows why it is being made. The alternative
    /// — threading the reason through the generated client — is not available,
    /// and passing it on the URL or a header would send it to prdb.
    /// </remarks>
    private static readonly AsyncLocal<PrdbWork?> Doing = new();

    private readonly Lock gate = new();

    private PrdbBudget? reading;
    private DateTimeOffset readAt;
    private DateTimeOffset? spentUntil;
    private int? refusedWith;

    /// <summary>
    /// What prdb last said about the hourly window, or null if it has not been
    /// asked anything yet.
    /// </summary>
    public PrdbBudget? LastReading
    {
        get
        {
            lock (gate)
            {
                return Fresh() ? reading : null;
            }
        }
    }

    /// <summary>
    /// The status code prdb refused the key with — <c>401</c> or <c>403</c> —
    /// or null while it has not. ADR 0014 makes a permanent refusal a Gap at
    /// once rather than after three failures, and this is the fact that Gap is
    /// drawn from: nothing about a routine can express it, because after the
    /// first refusal the routines stop running rather than failing.
    /// </summary>
    public int? RefusedWith
    {
        get
        {
            lock (gate)
            {
                return refusedWith;
            }
        }
    }

    /// <summary>
    /// Marks everything done inside it as <paramref name="work"/>, for the
    /// handler to read. Nested scopes are not expected and the inner one wins.
    /// </summary>
    public IDisposable For(PrdbWork work) => new Scope(work);

    /// <summary>
    /// What the caller said it was doing, or the least privileged kind of work
    /// there is when nobody said.
    /// </summary>
    /// <remarks>
    /// Falling back downwards rather than upwards is the whole of the choice: a
    /// request nobody classified is a bug, and the two ways to be wrong about a
    /// bug are to let it spend a budget reserved for an arrived file, or to
    /// have it deferred until somebody notices it never runs. The second is the
    /// one that shows up.
    /// </remarks>
    public PrdbWork Current => Doing.Value ?? PrdbWork.Repair;

    /// <summary>
    /// How long prdb's last <c>429</c> said to wait, or null if nothing is
    /// holding the budget shut.
    /// </summary>
    public TimeSpan? HeldFor()
    {
        lock (gate)
        {
            var now = time.GetUtcNow();

            return spentUntil is { } until && until > now ? until - now : null;
        }
    }

    /// <summary>Whether a request for <paramref name="work"/> may be sent now.</summary>
    public GovernorVerdict Ask(PrdbWork work)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();

            // A person typing a key is above all of it, including a refusal and
            // a spent budget: this is the request that finds out whether the key
            // works, and refusing to send it makes a wrong key unfixable from
            // the inside. It costs one request and is never made unattended.
            if (work == PrdbWork.Verification)
            {
                return GovernorVerdict.Send;
            }

            if (refusedWith is { } refused)
            {
                return GovernorVerdict.Defer(
                    Backoff.Longest,
                    $"prdb refused the key with {refused}");
            }

            if (spentUntil is { } until && until > now)
            {
                return GovernorVerdict.Defer(until - now, "the hourly budget is spent");
            }

            if (!Fresh() || reading is not { } budget)
            {
                // Nothing has been read yet, or what was read has aged out of
                // the window it described. The request is how the next reading
                // arrives.
                return GovernorVerdict.Send;
            }

            return budget.Admits(work)
                ? GovernorVerdict.Send
                : GovernorVerdict.Defer(
                    budget.WaitBefore(work),
                    $"{budget.Remaining} of {budget.Limit} requests left this hour, "
                    + $"and {work} is held back below {budget.ReserveFor(work)}");
        }
    }

    /// <summary>
    /// Reads what the answer said about the budget. Every metered response
    /// carries it, so the tool paces itself off the answers it is already
    /// getting rather than spending a request on <c>GET /rate-limit</c> to ask.
    /// </summary>
    public void Observe(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;

        lock (gate)
        {
            var now = time.GetUtcNow();

            if (ReadHourlyWindow(response) is { } window)
            {
                reading = window;
                readAt = now;
            }

            switch (status)
            {
                case 429:
                    // ADR 0014: Retry-After overrides the backoff exactly. Read
                    // only in its delta-seconds form, which is the form prdb
                    // documents; the HTTP-date form would need the clock read
                    // against somebody else's, and there is no reason to.
                    var wait = RetryAfterFrom(response) ?? PrdbBudget.ShortestWait;
                    spentUntil = now + wait;

                    logger.LogInformation(
                        "prdb says the budget is spent. Nothing is sent for {Seconds}s.",
                        (int)wait.TotalSeconds);
                    break;

                case 401 or 403:
                    // A settled answer. ADR 0014 stops the routines rather than
                    // retrying it, because retrying a settled answer buys
                    // nothing and spends the budget finding that out.
                    if (refusedWith != status)
                    {
                        logger.LogWarning(
                            "prdb refused the key with {Status}. Nothing is sent until a key works.",
                            status);
                    }

                    refusedWith = status;
                    break;

                default:
                    if (status < 400)
                    {
                        // The key works, whatever it did last. This is the way
                        // back from a refusal: a person saves a key, ADR 0010's
                        // check goes out under Verification, and it answers.
                        if (refusedWith is not null)
                        {
                            logger.LogInformation("prdb accepted a request again. The refusal is lifted.");
                        }

                        refusedWith = null;
                        spentUntil = null;
                    }

                    break;
            }
        }
    }

    private bool Fresh() => reading is not null && time.GetUtcNow() - readAt < ReadingKeeps;

    private static PrdbBudget? ReadHourlyWindow(HttpResponseMessage response)
    {
        if (Header(response, "X-RateLimit-Limit-Hour") is not { } limit
            || Header(response, "X-RateLimit-Remaining-Hour") is not { } remaining
            || Header(response, "X-RateLimit-Reset-Hour") is not { } reset)
        {
            // Lenient on purpose: the headers are metadata about a call that has
            // already been made, so a missing one is "no reading" rather than a
            // failure. 401, 403 and 503 carry none, because prdb did not meter
            // them.
            return null;
        }

        return new PrdbBudget(limit, remaining, TimeSpan.FromSeconds(reset));
    }

    private static int? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
        && int.TryParse(
            values.FirstOrDefault(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static TimeSpan? RetryAfterFrom(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta
        ?? (response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), out var seconds)
            && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : null);

    private sealed class Scope : IDisposable
    {
        private readonly PrdbWork? outer;

        public Scope(PrdbWork work)
        {
            outer = Doing.Value;
            Doing.Value = work;
        }

        public void Dispose() => Doing.Value = outer;
    }
}

/// <summary>What the governor said about one request.</summary>
public sealed record GovernorVerdict(bool Sends, TimeSpan Wait, string? Reason)
{
    public static GovernorVerdict Send { get; } = new(Sends: true, TimeSpan.Zero, Reason: null);

    public static GovernorVerdict Defer(TimeSpan wait, string reason) => new(Sends: false, wait, reason);
}
