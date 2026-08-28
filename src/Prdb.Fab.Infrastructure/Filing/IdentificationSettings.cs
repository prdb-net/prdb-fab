using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Filing;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Reads and replaces the one active after-download confidence set.</summary>
public sealed class IdentificationSettings(FabDbContext context)
{
    public async Task<AfterDownloadGateChoice> ReadAsync(CancellationToken cancellationToken)
    {
        var held = await context.GateAdmissions
            .Where(row => row.Gate == AfterDownloadGate.Name)
            .Select(row => row.Confidence)
            .ToListAsync(cancellationToken);

        var set = held.ToHashSet();
        if (set.SetEquals(AfterDownloadGate.Admissions(AfterDownloadGateChoice.ExactOnly)))
        {
            return AfterDownloadGateChoice.ExactOnly;
        }

        if (set.SetEquals(AfterDownloadGate.Admissions(AfterDownloadGateChoice.ExactAndStrong)))
        {
            return AfterDownloadGateChoice.ExactAndStrong;
        }

        throw new InvalidOperationException("The AfterDownload Gate carries an unsupported confidence set.");
    }

    public async Task<int> SaveAsync(
        AfterDownloadGateChoice choice,
        CancellationToken cancellationToken)
    {
        var admissions = AfterDownloadGate.Admissions(choice);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.GateAdmissions
            .Where(row => row.Gate == AfterDownloadGate.Name)
            .ExecuteDeleteAsync(cancellationToken);
        context.GateAdmissions.AddRange(admissions.Select(confidence => new GateAdmissionRow
        {
            Gate = AfterDownloadGate.Name,
            Confidence = confidence,
        }));

        // Only named answers waiting on this gate are reconsidered. Local
        // reasons and files already being or having been filed are deliberately
        // outside this set.
        var waiting = await context.ArrivingFiles
            .AsTracking()
            .Where(row => row.VideoId != null
                && row.Confidence != null
                && (row.State == ArrivingFileState.AwaitingFiling
                    || (row.State == ArrivingFileState.AwaitingIdentification
                        && row.Reason == ArrivingFileReason.Unidentified)))
            .ToListAsync(cancellationToken);

        foreach (var arrival in waiting)
        {
            var accepted = admissions.Contains(arrival.Confidence!.Value);
            arrival.State = accepted
                ? ArrivingFileState.AwaitingFiling
                : ArrivingFileState.AwaitingIdentification;
            arrival.Reason = accepted ? null : ArrivingFileReason.Unidentified;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return waiting.Count;
    }
}
