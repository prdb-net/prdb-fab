using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Core.Automation;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>Follows SABnzbd jobs and advances failed Videos through their ranking.</summary>
public sealed class DownloadFollowingRoutine(
    FabDbContext context,
    SabnzbdGateway sabnzbd,
    PersonDownloads personDownloads) : IRoutine
{
    public const string RoutineName = "Download following";
    private const int BatchSize = 100;

    public string Name => RoutineName;
    public Lane Lane => Lane.Live;
    public TimeSpan Cadence => TimeSpan.FromSeconds(5);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var outstanding = await context.Downloads
            .AsNoTracking()
            .Where(row => row.State == DownloadState.Outstanding)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var handled = 0;
        if (outstanding.Count > 0)
        {
            var installation = await context.Installation.AsNoTracking().Select(row => new
            {
                row.SabnzbdUrl,
                row.SabnzbdApiKey,
            }).SingleAsync(cancellationToken);

            if (installation.SabnzbdUrl is null || installation.SabnzbdApiKey is null)
            {
                return RunResult.Failed("SABnzbd is not configured, so outstanding Downloads cannot be followed.");
            }

            var knownIds = outstanding
                .Select(row => row.NzoId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToArray();
            var observation = await sabnzbd.ObserveAsync(
                installation.SabnzbdUrl,
                installation.SabnzbdApiKey,
                knownIds,
                outstanding.Any(row => row.NzoId is null),
                cancellationToken);

            // A failed read and an installation-wide pause are conditions of
            // the installation. Neither is evidence about any release.
            if (observation.Outcome != SabnzbdConnectionOutcome.Saved)
            {
                return RunResult.Failed("SABnzbd did not answer the Download following poll.");
            }

            if (observation.Paused)
            {
                return RunResult.Failed("SABnzbd is paused; no Download state was changed.");
            }

            var jobs = observation.Queue.Concat(observation.History).ToArray();
            var byId = jobs
                .Where(job => job.NzoId.Length > 0)
                .GroupBy(job => job.NzoId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var download in outstanding)
            {
                var job = download.NzoId is { Length: > 0 } nzoId
                    ? byId.GetValueOrDefault(nzoId)
                    : UniqueExactName(jobs, download.SubmittedName);

                if (job is null)
                {
                    var result = DownloadFollowing.Absent(download.ConsecutiveAbsences);
                    if (await UpdateOutstandingAsync(download, result, null, cancellationToken) > 0)
                    {
                        await QueueAutomaticRetryAsync(download, result, cancellationToken);
                        handled++;
                    }
                    continue;
                }

                var found = DownloadFollowing.Found(SignalOf(job));
                if (await UpdateOutstandingAsync(download, found, job, cancellationToken) > 0)
                {
                    await QueueAutomaticRetryAsync(download, found, cancellationToken);
                    handled++;
                }
            }
        }

        var retryBudget = await context.Installation
            .Select(row => row.RetryBudget)
            .SingleAsync(cancellationToken);
        var retryVideos = await context.Downloads
            .AsNoTracking()
            .GroupBy(row => row.VideoId)
            .Where(group => group.Count() < retryBudget
                && group.All(row => row.State == DownloadState.Failed && row.OriginIsPerson))
            .OrderBy(group => group.Min(row => row.CreatedAt))
            .Select(group => group.Key)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var videoId in retryVideos)
        {
            var verdict = await personDownloads.RetryNextAsync(videoId, cancellationToken);
            if (verdict?.Outcome is DownloadOutcome.ConnectionProblem or DownloadOutcome.IndexerProblem)
            {
                return RunResult.Failed(verdict.Detail);
            }

            if (verdict?.Outcome is DownloadOutcome.Submitted
                or DownloadOutcome.SubmissionUnknown
                or DownloadOutcome.Rejected)
            {
                handled++;
            }
        }

        return handled == 0 ? RunResult.NothingToDo : RunResult.Handled(handled);
    }

    private static SabnzbdJob? UniqueExactName(IEnumerable<SabnzbdJob> jobs, string submittedName)
    {
        var matches = jobs
            .Where(job => string.Equals(job.Name, submittedName, StringComparison.Ordinal))
            .DistinctBy(job => job.NzoId)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static DownloadSignal SignalOf(SabnzbdJob job)
    {
        if (job.Location == SabnzbdJobLocation.Queue
            && string.Equals(job.Status, "Paused", StringComparison.OrdinalIgnoreCase)
            && job.Labels.Any(label => label is "ENCRYPTED" or "UNWANTED"))
        {
            return DownloadSignal.Unusable;
        }

        if (job.Location == SabnzbdJobLocation.History
            && string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return DownloadSignal.Completed;
        }

        return job.Location == SabnzbdJobLocation.History
            && string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase)
                ? DownloadSignal.Failed
                : DownloadSignal.Outstanding;
    }

    private Task<int> UpdateOutstandingAsync(
        DownloadRow row,
        DownloadFollowResult result,
        SabnzbdJob? job,
        CancellationToken cancellationToken)
    {
        var nzoId = row.NzoId ?? (job?.NzoId is { Length: > 0 } id ? id : null);
        var storage = job?.Location == SabnzbdJobLocation.History
            && !string.IsNullOrWhiteSpace(job.Storage)
                ? job.Storage
                : row.Storage;
        return context.Downloads
            .Where(candidate => candidate.Id == row.Id && candidate.State == DownloadState.Outstanding)
            .ExecuteUpdateAsync(update => update
                .SetProperty(candidate => candidate.NzoId, nzoId)
                .SetProperty(candidate => candidate.LastSabnzbdStatus, job == null ? row.LastSabnzbdStatus : job.Status)
                .SetProperty(candidate => candidate.FailMessage, job == null ? row.FailMessage : job.FailMessage)
                .SetProperty(candidate => candidate.StageLog, job == null ? row.StageLog : job.StageLog)
                .SetProperty(candidate => candidate.Storage, storage)
                .SetProperty(candidate => candidate.State, result.State)
                .SetProperty(candidate => candidate.Cause, result.Cause)
                .SetProperty(candidate => candidate.ConsecutiveAbsences, result.ConsecutiveAbsences),
                cancellationToken);
    }

    private async Task QueueAutomaticRetryAsync(
        DownloadRow download,
        DownloadFollowResult result,
        CancellationToken cancellationToken)
    {
        if (download.OriginIsPerson || result.State != DownloadState.Failed) return;

        var localVideoId = await context.CatalogueVideos
            .Where(row => row.PrdbId == download.VideoId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (localVideoId is null) return;

        await context.Releases
            .Where(row => row.VideoId == localVideoId
                && row.IdentificationState == Prdb.Fab.Core.ReleaseDiscovery.IdentificationState.Matched)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.AutomationPending, true)
                .SetProperty(row => row.AutomationDecisionReason, (AutomationDecisionReason?)null),
                cancellationToken);
    }
}
