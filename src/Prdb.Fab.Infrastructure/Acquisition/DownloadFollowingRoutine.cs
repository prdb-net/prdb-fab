using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

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
            .AsTracking()
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
                    Apply(download, DownloadFollowing.Absent(download.ConsecutiveAbsences));

                    handled++;
                    continue;
                }

                if (download.NzoId is null && job.NzoId.Length > 0)
                {
                    download.NzoId = job.NzoId;
                }

                download.LastSabnzbdStatus = job.Status;
                download.FailMessage = job.FailMessage;
                download.StageLog = job.StageLog;

                Apply(download, DownloadFollowing.Found(SignalOf(job)));

                handled++;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        var retryBudget = await context.Installation
            .Select(row => row.RetryBudget)
            .SingleAsync(cancellationToken);
        var retryVideos = await context.Downloads
            .AsNoTracking()
            .GroupBy(row => row.VideoId)
            .Where(group => group.Count() < retryBudget
                && group.All(row => row.State == DownloadState.Failed))
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

    private static void Apply(DownloadRow row, DownloadFollowResult result)
    {
        row.State = result.State;
        row.Cause = result.Cause;
        row.ConsecutiveAbsences = result.ConsecutiveAbsences;
    }
}
