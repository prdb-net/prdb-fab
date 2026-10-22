using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Proves 14 days first, extends to 30, then admits optional history up to 90.</summary>
public sealed class IndexerRecentWindowRoutine(FabDbContext context, IndexerSearch search,
    ReleaseRows releases, PrdbGovernor governor, TimeProvider time,
    ILogger<IndexerRecentWindowRoutine> logger) : IRoutine, ITargetedRoutine
{
    public string Name => DiscoveryRoutineNames.RecentWindow;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(30);
    public Task<IReadOnlyList<string>> TargetsAsync(CancellationToken token) => IndexerTargets.CanonicalAsync(
        context.Indexers.Where(row => row.Enabled).Select(row => row.Id), token);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target, out var indexerId)) return RunResult.NothingToDo;
        var legacy = await context.IndexerWalkStates.AsTracking().SingleOrDefaultAsync(row => row.IndexerId == indexerId, cancellationToken);
        if (legacy is null || DiscoveryState.DeserialiseNames(legacy.MissingCategoryNames).Count > 0) return RunResult.NothingToDo;
        var now = time.GetUtcNow();
        var windows = await context.IndexerWindows.AsTracking().Where(row => row.IndexerId == indexerId)
            .OrderBy(row => row.Days).ToListAsync(cancellationToken);
        if (windows.Count == 0)
        {
            // Upgrade completed proofs without deleting cached Releases. Legacy
            // numeric offsets have no identity anchor and cannot safely prove a
            // new range; the first anchored pass starts at the head once.
            foreach (var days in new[] { 14, 30, 90 })
                windows.Add(new IndexerWindowRow { IndexerId = indexerId, Days = days,
                    CompletedAt = legacy.RecentWindowCompletedAt, ProofStartedAt = legacy.RecentWindowCompletedAt,
                    OldestPostDate = legacy.RecentWindowOldestPostDate });
            context.IndexerWindows.AddRange(windows);
            await context.SaveChangesAsync(cancellationToken);
        }
        var due = windows.FirstOrDefault(row => !Fresh(row, now));
        if (due is null) return RunResult.NothingToDo;
        var purpose = due.Days switch
        {
            14 => IndexerQueryPurpose.MinimumWindow,
            30 => IndexerQueryPurpose.WorkingWindow,
            _ => IndexerQueryPurpose.OptionalHistory,
        };
        if (due.Days == 90 && (!governor.HasReading || !governor.Ask(PrdbWork.ReleaseIdentification).Sends
            || await context.Releases.Where(row => row.IdentificationState == IdentificationState.Awaiting
                || row.IdentificationDetailsPending).Select(row => row.Title).Distinct().CountAsync(cancellationToken) > ReleaseIdentificationRoutine.BatchSize * 2))
            return RunResult.Deferred(Earlier(TimeSpan.FromMinutes(1), MinimumDueIn(windows, now)), "Optional Indexer history waits for downstream Identification capacity.");

        if (due.PassStartedAt is null)
        {
            due.PassStartedAt = now;
            due.PassQueries = 0;
            due.ResumeOffset = 0;
            due.AnchorReleaseId = null;
            due.OldestPostDate = null;
            var shorter = windows.LastOrDefault(row => row.Days < due.Days && Fresh(row, now));
            if (shorter?.AnchorReleaseId is not null)
            {
                due.PassStartedAt = shorter.ProofStartedAt ?? now;
                due.ResumeOffset = shorter.ResumeOffset;
                due.AnchorReleaseId = shorter.AnchorReleaseId;
                due.OldestPostDate = shorter.OldestPostDate;
            }
        }
        var offset = Math.Max(0, due.ResumeOffset - IndexerWindowPolicy.ResumeOverlap);
        var searched = await search.PageAsync(indexerId, 0, due.Days, purpose, null, cancellationToken, offset);
        if (searched.DeferredFor is { } wait)
            return RunResult.Deferred(due.Days == 14 ? wait : Earlier(wait, MinimumDueIn(windows, now)), $"Indexer {due.Days}-day coverage waits for protected Daily Query Budget.");
        var read = searched.Read!;
        if (read.Refusal is not null) return RunResult.Failed("The Indexer refused its coverage pass.", read.RetryAfter);
        due.PassQueries++;
        var write = await releases.UpsertAsync(indexerId, read.Releases, now, ReleaseSource.IndexerWalk, cancellationToken);
        if (due.AnchorReleaseId is not null && !read.Releases.Any(row => row.DerivedReleaseId == due.AnchorReleaseId))
        {
            // Offset drift exceeded our overlap (or the anchor disappeared).
            // Cached rows survive; no coverage is asserted across that gap.
            due.PassStartedAt = null;
            due.ResumeOffset = 0;
            due.AnchorReleaseId = null;
            await context.SaveChangesAsync(cancellationToken);
            return RunResult.Discovered(read.Releases.Count + read.DroppedWithoutIdentity, write.Added, TimeSpan.Zero);
        }
        var returned = read.Releases.Count + read.DroppedWithoutIdentity;
        var oldest = read.Releases.Count == 0 ? (DateTimeOffset?)null : read.Releases.Min(row => row.PostDate);
        due.OldestPostDate = Oldest(due.OldestPostDate, oldest);
        due.ResumeOffset = offset + returned;
        due.AnchorReleaseId = read.Releases.LastOrDefault()?.DerivedReleaseId;
        var complete = returned < NewznabGateway.PageSize || oldest <= due.PassStartedAt!.Value.AddDays(-due.Days);
        if (complete)
        {
            due.CompletedAt = now;
            due.ProofStartedAt = due.PassStartedAt;
            due.LastPassQueries = due.PassQueries;
            due.PassStartedAt = null;
            if (due.Days == 14) legacy.MinimumPassQueries = due.LastPassQueries;
            logger.LogInformation("Indexer coverage completed {Days} days using {Queries} queries.", due.Days, due.LastPassQueries);
        }
        // Retain the compatibility projection for old local consumers; only a
        // completed 90-day pass can write its old strong completion marker.
        legacy.RecentWindowPassStartedAt = due.PassStartedAt;
        legacy.RecentWindowResumePage = due.ResumeOffset / NewznabGateway.PageSize;
        legacy.RecentWindowOldestPostDate = due.OldestPostDate;
        if (due.Days == 90 && complete) legacy.RecentWindowCompletedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        var remains = windows.Any(row => !Fresh(row, now));
        return RunResult.Discovered(returned, write.Added, remains ? TimeSpan.Zero : MinimumDueIn(windows, now));
    }

    private static TimeSpan Earlier(TimeSpan left, TimeSpan right) => left <= right ? left : right;
    private static TimeSpan MinimumDueIn(IReadOnlyList<IndexerWindowRow> windows, DateTimeOffset now)
    {
        var minimum = windows.Single(row => row.Days == 14);
        var at = minimum.ProofStartedAt ?? minimum.CompletedAt ?? now;
        var wait = at + IndexerWindowPolicy.FreshFor - now + TimeSpan.FromSeconds(1);
        return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(1);
    }

    internal static bool Fresh(IndexerWindowRow row, DateTimeOffset now) =>
        row.CompletedAt >= now - IndexerWindowPolicy.FreshFor && row.ProofStartedAt >= now - IndexerWindowPolicy.FreshFor;
    private static DateTimeOffset? Oldest(DateTimeOffset? held, DateTimeOffset? read) => held is null ? read : read is null || held <= read ? held : read;
}
