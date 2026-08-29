using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>Converges crash-safe manual Download reservations into SABnzbd.</summary>
public sealed class DownloadSubmissionRoutine(
    FabDbContext context,
    PersonDownloads downloads) : IRoutine
{
    public const string RoutineName = "downloads.submit-pending";

    public string Name => RoutineName;
    public Lane Lane => Lane.Live;
    public TimeSpan Cadence => TimeSpan.FromSeconds(5);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var next = await context.Downloads
            .Where(row => row.SubmissionState == DownloadSubmissionState.Pending)
            .OrderBy(row => row.CreatedAt)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is null) return RunResult.NothingToDo;

        var verdict = await downloads.SubmitPendingAsync(next.Value, cancellationToken);
        return verdict?.Outcome is DownloadOutcome.Submitted or DownloadOutcome.Rejected
            ? RunResult.Handled(1)
            : RunResult.Failed(verdict?.Detail ?? "The pending Download disappeared.");
    }
}
