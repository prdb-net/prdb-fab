using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.Automation;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Reads and replaces ADR 0006's two active named confidence sets.</summary>
public sealed class IdentificationSettings(FabDbContext context)
{
    public async Task<IdentificationGateChoices> ReadAsync(CancellationToken cancellationToken)
    {
        var held = await context.GateAdmissions.ToListAsync(cancellationToken);

        return new(
            BeforeChoice(held.Where(row => row.Gate == BeforeDownloadGate.Name).Select(row => row.Confidence)),
            AfterChoice(held.Where(row => row.Gate == AfterDownloadGate.Name).Select(row => row.Confidence)));
    }

    private static BeforeDownloadGateChoice BeforeChoice(IEnumerable<Prdb.Fab.Core.ReleaseDiscovery.IdentificationConfidence> held)
    {
        var set = held.ToHashSet();
        foreach (var choice in Enum.GetValues<BeforeDownloadGateChoice>())
        {
            if (set.SetEquals(BeforeDownloadGate.Admissions(choice))) return choice;
        }

        throw new InvalidOperationException("The BeforeDownload Gate carries an unsupported confidence set.");
    }

    private static AfterDownloadGateChoice AfterChoice(IEnumerable<Prdb.Fab.Core.ReleaseDiscovery.IdentificationConfidence> held)
    {
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

    public async Task<IdentificationGateSave> SaveAsync(
        BeforeDownloadGateChoice beforeChoice,
        AfterDownloadGateChoice afterChoice,
        CancellationToken cancellationToken)
    {
        var beforeAdmissions = BeforeDownloadGate.Admissions(beforeChoice);
        var afterAdmissions = AfterDownloadGate.Admissions(afterChoice);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.GateAdmissions
            .Where(row => row.Gate == BeforeDownloadGate.Name || row.Gate == AfterDownloadGate.Name)
            .ExecuteDeleteAsync(cancellationToken);
        context.GateAdmissions.AddRange(beforeAdmissions.Select(confidence => new GateAdmissionRow
        {
            Gate = BeforeDownloadGate.Name,
            Confidence = confidence,
        }));
        context.GateAdmissions.AddRange(afterAdmissions.Select(confidence => new GateAdmissionRow
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
            var accepted = afterAdmissions.Contains(arrival.Confidence!.Value);
            arrival.State = accepted
                ? ArrivingFileState.AwaitingFiling
                : ArrivingFileState.AwaitingIdentification;
            arrival.Reason = accepted ? null : ArrivingFileReason.Unidentified;
        }

        await context.SaveChangesAsync(cancellationToken);

        // The request only changes state. The ordinary Decide routine performs
        // every submission, so changing a gate cannot turn an HTTP request into
        // a remote SABnzbd write.
        var automatic = await context.Releases
            .Where(release => release.VideoId != null
                && release.IdentificationState == Prdb.Fab.Core.ReleaseDiscovery.IdentificationState.Matched
                && context.WantedVideos.Any(wanted => wanted.VideoId == release.VideoId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(release => release.AutomationPending, true)
                .SetProperty(release => release.AutomationDecisionReason, (Prdb.Fab.Core.Automation.AutomationDecisionReason?)null),
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new(waiting.Count, automatic);
    }
}

public sealed record IdentificationGateChoices(
    BeforeDownloadGateChoice BeforeDownload,
    AfterDownloadGateChoice AfterDownload);

public sealed record IdentificationGateSave(int ArrivingFilesReconsidered, int ReleasesReconsidered);
