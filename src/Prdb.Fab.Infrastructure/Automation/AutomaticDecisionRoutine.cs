using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Automation;

/// <summary>Drains the one bounded Decide work set into the shared Download path.</summary>
public sealed class AutomaticDecisionRoutine(
    FabDbContext context,
    ReleaseRankings rankings,
    AutomaticEligibility eligibility,
    PersonDownloads downloads,
    TimeProvider time) : IRoutine
{
    public const string RoutineName = "Automatic decisions";
    public const int BatchSize = 25;

    public string Name => RoutineName;
    public Lane Lane => Lane.Bulk;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var videoIds = await context.Releases
            .Where(row => row.AutomationPending
                && row.VideoId != null
                && row.IdentificationState == IdentificationState.Matched)
            .GroupBy(row => row.VideoId!.Value)
            .Select(group => new
            {
                VideoId = group.Key,
                LastDecisionAt = group.Min(row => row.AutomationDecisionAt),
                FirstSeenAt = group.Min(row => row.FirstSeenAt),
            })
            .OrderBy(row => row.LastDecisionAt ?? row.FirstSeenAt)
            .ThenBy(row => row.VideoId)
            .Take(BatchSize)
            .Select(row => row.VideoId)
            .ToListAsync(cancellationToken);
        if (videoIds.Count == 0) return RunResult.NothingToDo;

        var now = time.GetUtcNow();
        await context.ReleasesNotDownloaded
            .Where(row => row.At < now.AddDays(-7))
            .ExecuteDeleteAsync(cancellationToken);

        var handled = 0;
        foreach (var localVideoId in videoIds)
        {
            var videoId = await context.CatalogueVideos
                .Where(row => row.Id == localVideoId)
                .Select(row => row.PrdbId)
                .SingleAsync(cancellationToken);
            var pending = await context.Releases
                .AsTracking()
                .Where(row => row.VideoId == localVideoId
                    && row.AutomationPending
                    && row.IdentificationState == IdentificationState.Matched)
                .OrderBy(row => row.FirstSeenAt)
                .ThenBy(row => row.Id)
                .ToListAsync(cancellationToken);
            var ranking = await rankings.ForVideoAsync(videoId, observeDecision: false, cancellationToken);
            if (ranking is null)
            {
                SetReasons(pending, new Dictionary<long, AutomaticReleaseEligibility>(), now);
                await context.SaveChangesAsync(cancellationToken);
                handled++;
                continue;
            }

            var choices = pending
                .Select(row => ranking.Find(row.Id))
                .OfType<ReleaseChoice>()
                .ToArray();
            var decisions = await eligibility.ForVideoAsync(videoId, choices, cancellationToken);
            var selected = ranking.Ranked.FirstOrDefault(choice =>
                decisions.TryGetValue(choice.Id, out var decision) && decision.Eligible);

            if (selected is null)
            {
                SetReasons(pending, decisions, now, ranking);
                await context.SaveChangesAsync(cancellationToken);
                handled++;
                continue;
            }

            var verdict = await downloads.AutomaticDownloadAsync(
                Guid.CreateVersion7(now),
                videoId,
                selected.Id,
                cancellationToken);
            if (verdict?.Outcome is DownloadOutcome.ConnectionProblem or DownloadOutcome.IndexerProblem)
                return RunResult.Failed(verdict.Detail);

            if (verdict?.Outcome is DownloadOutcome.Submitted or DownloadOutcome.SubmissionUnknown)
            {
                foreach (var release in pending)
                {
                    release.AutomationPending = false;
                    release.AutomationDecisionReason = null;
                    release.AutomationDecisionAt = now;
                }
            }
            else if (verdict?.Outcome == DownloadOutcome.Rejected)
            {
                // The consumed Release is excluded by the existing Ranking on
                // the next turn. Keeping the work set live makes that turn pick
                // the next Release without a second retry implementation.
                foreach (var release in pending) release.AutomationPending = true;
            }
            else
            {
                // A current fact changed between evaluation and reservation.
                // Leave the durable work set standing and read it again.
                foreach (var release in pending) release.AutomationPending = true;
            }

            await context.SaveChangesAsync(cancellationToken);
            handled++;
        }

        return RunResult.Handled(handled);
    }

    private void SetReasons(
        IReadOnlyList<ReleaseRow> releases,
        IReadOnlyDictionary<long, AutomaticReleaseEligibility> decisions,
        DateTimeOffset now,
        VideoReleaseRanking? ranking = null)
    {
        var rankedIds = ranking?.Ranked.Select(choice => choice.Id).ToHashSet() ?? [];
        foreach (var release in releases)
        {
            var decision = decisions.GetValueOrDefault(release.Id);
            var reason = decision?.Reason
                ?? (rankedIds.Contains(release.Id)
                    ? AutomationDecisionReason.NotWanted
                    : AutomationDecisionReason.NoReleasesLeft);
            if (decision?.Eligible == true && !rankedIds.Contains(release.Id))
                reason = AutomationDecisionReason.NoReleasesLeft;

            var changed = release.AutomationDecisionReason != reason;
            release.AutomationDecisionReason = reason;
            release.AutomationDecisionAt = now;
            release.AutomationPending = decision?.Wait == true;

            if (changed && reason != AutomationDecisionReason.NotWanted)
            {
                context.ReleasesNotDownloaded.Add(new ReleaseNotDownloadedRow
                {
                    At = now,
                    Reason = reason.ToString(),
                });
            }
        }
    }
}
