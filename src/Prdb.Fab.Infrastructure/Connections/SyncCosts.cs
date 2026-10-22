using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Connections;

/// <summary>
/// Bounded process-local diagnostics. Keys are enums and a closed endpoint set:
/// no URL, account identifier, input name, body or credential is retained.
/// A restart starts a new measurement interval rather than inventing continuity.
/// </summary>
public sealed class SyncCosts(TimeProvider time)
{
    private readonly Lock gate = new();
    private readonly Dictionary<(PrdbWork Work, string Endpoint, int Status), RequestCost> requests = [];
    private readonly Dictionary<PrdbWork, QuotaWait> waits = [];
    private readonly DateTimeOffset since = time.GetUtcNow();
    private long identified;
    private long reused;
    private long avoidedDetails;

    public void Request(PrdbWork work, string? path, int status, TimeSpan elapsed, long bytes)
    {
        // Deliberately classify before retaining anything. Even an unexpected
        // SDK route containing a user value becomes the single Other bucket.
        var endpoint = path switch
        {
            "/videos/identify" => "Identify",
            "/videos/batch" => "VideoDetails",
            "/rate-limit" => "RateLimit",
            "/user-identity" => "Verification",
            "/videos" => "VideoDiscovery",
            _ when path?.EndsWith("/changes", StringComparison.Ordinal) == true => "Changes",
            _ => "Other",
        };
        var statusClass = status == 429 ? 429 : status / 100 * 100;
        lock (gate)
        {
            var key = (work, endpoint, statusClass);
            requests.TryGetValue(key, out var before);
            requests[key] = new(work, endpoint, statusClass, (before?.Requests ?? 0) + 1,
                (before?.ElapsedMilliseconds ?? 0) + elapsed.TotalMilliseconds,
                (before?.PayloadBytes ?? 0) + bytes);
        }
    }

    public void Deferred(PrdbWork work, TimeSpan wait)
    {
        lock (gate)
        {
            waits.TryGetValue(work, out var before);
            waits[work] = new(work, (before?.Deferrals ?? 0) + 1,
                (before?.ScheduledWaitSeconds ?? 0) + wait.TotalSeconds);
        }
    }

    public void Identification(int inputs, int reusedAnswers, int detailsAvoided)
    {
        lock (gate)
        {
            identified += inputs;
            reused += reusedAnswers;
            avoidedDetails += detailsAvoided;
        }
    }

    public SyncCostSnapshot Snapshot()
    {
        lock (gate)
        {
            return new(since, [.. requests.Values], [.. waits.Values], identified, reused, avoidedDetails);
        }
    }
}

public sealed record RequestCost(PrdbWork Work, string Endpoint, int StatusClass,
    long Requests, double ElapsedMilliseconds, long PayloadBytes);
public sealed record QuotaWait(PrdbWork Work, long Deferrals, double ScheduledWaitSeconds);
public sealed record SyncCostSnapshot(DateTimeOffset Since, IReadOnlyList<RequestCost> Requests,
    IReadOnlyList<QuotaWait> Waits, long IdentifiedInputs, long ReusedAnswers, long DetailFetchesAvoided);
