using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>Admission from shared account counters, never a locally replenished allowance.</summary>
public sealed class PrdbGovernor(TimeProvider time, ILogger<PrdbGovernor> logger)
{
    private static readonly AsyncLocal<PrdbWork?> Doing = new();
    private readonly Lock gate = new();
    private PrdbBudget? hourly;
    private PrdbBudget? monthly;
    private DateTimeOffset readAt;
    private DateTimeOffset refreshAfter;
    private DateTimeOffset? spentUntil;
    private DateTimeOffset nextBackgroundAt;
    private DateTimeOffset backgroundDebitedAt;
    private double backgroundCredits;
    private int? refusedWith;
    private string? subscriptions;
    private DateTimeOffset? identityReadAt;
    private string? keyDigest;

    // Serial transport admission makes counters and debit atomic across lanes.
    // No transaction spans HTTP and no new network concurrency is introduced.
    internal SemaphoreSlim OperationGate { get; } = new(1, 1);
    internal SemaphoreSlim TransportGate { get; } = new(1, 1);
    internal SemaphoreSlim RefreshGate { get; } = new(1, 1);
    public SyncCosts Costs { get; } = new(time);
    public PrdbBudget? LastReading { get { lock (gate) return Fresh() ? hourly : null; } }
    public int? RefusedWith { get { lock (gate) return refusedWith; } }
    public bool NeedsRefresh { get { lock (gate) return !Fresh() && time.GetUtcNow() >= refreshAfter; } }
    public bool HasReading { get { lock (gate) return Fresh(); } }
    public IDisposable For(PrdbWork work) => new Scope(work);
    public PrdbWork Current => Doing.Value ?? PrdbWork.Repair;

    public PrdbQuotaSnapshot Snapshot()
    {
        lock (gate) return new(hourly, monthly, hourly is null ? null : readAt, Fresh(),
            spentUntil, nextBackgroundAt > time.GetUtcNow() ? nextBackgroundAt : null);
    }

    public string? Subscriptions { get { lock (gate) return subscriptions; } }
    public DateTimeOffset? IdentityReadAt { get { lock (gate) return identityReadAt; } }

    public void ObserveIdentity(UserIdentityResponse identity)
    {
        lock (gate)
        {
            subscriptions = identity.ActiveSubscriptions is { Count: > 0 } active
                ? string.Join(", ", active.Select(item => item.PackageTitle ?? item.PackageIdentifier ?? "Unnamed subscription"))
                : "No active subscription reported; API-reported limits still apply";
            identityReadAt = time.GetUtcNow();
        }
    }

    internal void UseKey(string key)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        lock (gate)
        {
            if (keyDigest == digest) return;
            keyDigest = digest;
            hourly = monthly = null;
            spentUntil = null;
            refusedWith = null;
            refreshAfter = default;
            nextBackgroundAt = backgroundDebitedAt = default;
            backgroundCredits = 0;
            subscriptions = null;
            identityReadAt = null;
            // A different key still has to read its remote account counters.
        }
    }

    public TimeSpan? HeldFor()
    {
        lock (gate) return spentUntil is { } until && until > time.GetUtcNow()
            ? until - time.GetUtcNow() : null;
    }

    public GovernorVerdict Ask(PrdbWork work)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            if (work == PrdbWork.Verification) return GovernorVerdict.Send;
            if (refusedWith is { } refused)
                return GovernorVerdict.Defer(Backoff.Longest, $"prdb refused the key with {refused}");
            if (spentUntil is { } until && until > now)
                return GovernorVerdict.Defer(until - now, "prdb requested a quota wait (Retry-After)");
            if (work == PrdbWork.BudgetReading) return GovernorVerdict.Send;
            if (!Fresh()) return GovernorVerdict.Defer(PrdbBudget.ShortestWait,
                "current hourly and monthly account counters are unavailable; waiting for a budget reading");

            foreach (var (budget, window) in new[] { (hourly!, "hourly"), (monthly!, "rolling monthly") })
            {
                var reserve = budget.ReserveFor(work);
                if (Background(work))
                {
                    var recurring = IdleProfile.ReservedPerWindow(hourly!.Limit, monthly!.Limit);
                    reserve += (int)Math.Ceiling(window == "hourly"
                        ? recurring / (24 * IdleProfile.WindowDays) : recurring / IdleProfile.WindowDays);
                }
                if (budget.Remaining <= reserve)
                    return GovernorVerdict.Defer(WaitForReading(budget),
                        $"{budget.Remaining} of {budget.Limit} account requests remain in the {window} window; {work} preserves {reserve}");
            }
            if (Background(work) && nextBackgroundAt > now)
                return GovernorVerdict.Defer(nextBackgroundAt - now,
                    "background work is paced by sustainable hourly and rolling monthly account capacity");
            return GovernorVerdict.Send;
        }
    }

    internal void Debit(PrdbWork work)
    {
        lock (gate)
        {
            if (work == PrdbWork.BudgetReading) return;
            if (Background(work) && hourly is { } hour && monthly is { } month)
            {
                // Rate comes from the reported plan, not an ever-shrinking
                // fraction of its remaining balance. Admission still checks the
                // actual shared balance before every request.
                var recurring = IdleProfile.ReservedPerWindow(hour.Limit, month.Limit);
                var available = Math.Max(0, month.Remaining - month.ReserveFor(work) - recurring);
                var seconds = Math.Max(3600d / Math.Max(1, hour.Limit * .8 - recurring / (24 * IdleProfile.WindowDays)),
                    TimeSpan.FromDays(IdleProfile.WindowDays).TotalSeconds
                    / Math.Max(1, month.Limit * .8 - recurring));
                var now = time.GetUtcNow();
                // A useful, bounded initial turn; subsequent credit is earned at
                // the sustainable rate. Restart cannot bypass remote reserves.
                var capacity = Math.Max(1, Math.Min(200, Math.Min(hour.Limit * .2, available * .02)));
                backgroundCredits = backgroundDebitedAt == default ? capacity
                    : Math.Min(capacity, backgroundCredits + (now - backgroundDebitedAt).TotalSeconds / seconds);
                backgroundCredits = Math.Max(0, backgroundCredits - 1);
                backgroundDebitedAt = now;
                nextBackgroundAt = backgroundCredits >= 1 ? now
                    : now + TimeSpan.FromSeconds(seconds * (1 - backgroundCredits));
            }
            if (hourly is { } h) hourly = h with { Remaining = Math.Max(0, h.Remaining - 1) };
            if (monthly is { } m) monthly = m with { Remaining = Math.Max(0, m.Remaining - 1) };
        }
    }

    public void Observe(HttpResponseMessage response)
    {
        lock (gate)
        {
            var hour = ReadWindow(response, "Hour");
            var month = ReadWindow(response, "Month");
            if (hour is not null) hourly = hour;
            if (month is not null) monthly = month;
            if (hour is not null && month is not null) readAt = time.GetUtcNow();
            switch ((int)response.StatusCode)
            {
                case 429:
                    spentUntil = time.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? PrdbBudget.ShortestWait);
                    logger.LogInformation("prdb requested a quota wait.");
                    break;
                case 401 or 403:
                    refusedWith = (int)response.StatusCode;
                    break;
                case < 400:
                    refusedWith = null;
                    // An unrelated successful response cannot cancel a 429 hold.
                    break;
            }
        }
    }

    internal void BeginRefresh()
    {
        lock (gate) refreshAfter = time.GetUtcNow() + PrdbBudget.ShortestWait;
    }

    internal void Observe(GetRateLimitResponse? response)
    {
        lock (gate)
        {
            if (From(response?.Hourly) is not { } hour || From(response?.Monthly) is not { } month) return;
            hourly = hour;
            monthly = month;
            readAt = time.GetUtcNow();
        }
    }

    private static PrdbBudget? From(RateLimitWindowStatus? window) =>
        window?.Limit is > 0 && window.Remaining is >= 0 && window.ResetsInSeconds is >= 0
            ? new(window.Limit.Value, Math.Min(window.Remaining.Value, window.Limit.Value),
                TimeSpan.FromSeconds(window.ResetsInSeconds.Value)) : null;

    private bool Fresh() => hourly is not null && monthly is not null
        && time.GetUtcNow() - readAt < TimeSpan.FromMinutes(1);

    private TimeSpan WaitForReading(PrdbBudget budget)
    {
        var next = readAt + (budget.ResetIn > PrdbBudget.ShortestWait ? budget.ResetIn : PrdbBudget.ShortestWait);
        return next > time.GetUtcNow() ? next - time.GetUtcNow() : PrdbBudget.ShortestWait;
    }

    private static bool Background(PrdbWork work) => work is PrdbWork.Repair or PrdbWork.ReleaseIdentification;
    private static PrdbBudget? ReadWindow(HttpResponseMessage response, string suffix)
    {
        if (Header(response, $"X-RateLimit-Limit-{suffix}") is not > 0
            || Header(response, $"X-RateLimit-Remaining-{suffix}") is not >= 0
            || Header(response, $"X-RateLimit-Reset-{suffix}") is not >= 0) return null;
        var limit = Header(response, $"X-RateLimit-Limit-{suffix}")!.Value;
        return new(limit, Math.Min(limit, Header(response, $"X-RateLimit-Remaining-{suffix}")!.Value),
            TimeSpan.FromSeconds(Header(response, $"X-RateLimit-Reset-{suffix}")!.Value));
    }
    private static int? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
        && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : null;

    private sealed class Scope : IDisposable
    {
        private readonly PrdbWork? outer = Doing.Value;
        public Scope(PrdbWork work) => Doing.Value = work;
        public void Dispose() => Doing.Value = outer;
    }
}

public sealed record PrdbQuotaSnapshot(PrdbBudget? Hourly, PrdbBudget? Monthly,
    DateTimeOffset? ObservedAt, bool Fresh, DateTimeOffset? RetryAfterUntil, DateTimeOffset? BackgroundDueAt);
public sealed record GovernorVerdict(bool Sends, TimeSpan Wait, string? Reason)
{
    public static GovernorVerdict Send { get; } = new(true, TimeSpan.Zero, null);
    public static GovernorVerdict Defer(TimeSpan wait, string reason) => new(false, wait, reason);
}
