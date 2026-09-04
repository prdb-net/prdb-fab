using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Reporting;

public sealed record ReportingSettingsState(
    bool ReportFulfilments,
    int FulfilmentBacklog,
    bool ReportConfirmedAssignments,
    int ConfirmedAssignmentBacklog);

/// <summary>The two independent Reporting channels.</summary>
public sealed class ReportingSettings(
    FabDbContext context,
    FulfilmentDifference fulfilments,
    IRoutineStore routines)
{
    public async Task<ReportingSettingsState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation
            .Select(row => new
            {
                row.PrdbUserHash,
                row.ReportFulfilments,
                row.ReportConfirmedAssignments,
            })
            .SingleAsync(cancellationToken);

        return await StateAsync(
            installation.PrdbUserHash,
            installation.ReportFulfilments,
            installation.ReportConfirmedAssignments,
            cancellationToken);
    }

    public async Task<ReportingSettingsState> SaveAsync(
        bool reportFulfilments,
        bool reportConfirmedAssignments,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation
            .AsTracking()
            .SingleAsync(cancellationToken);
        var enabled = (!installation.ReportFulfilments && reportFulfilments)
            || (!installation.ReportConfirmedAssignments && reportConfirmedAssignments);

        installation.ReportFulfilments = reportFulfilments;
        installation.ReportConfirmedAssignments = reportConfirmedAssignments;
        await context.SaveChangesAsync(cancellationToken);

        // Enabling only changes when the ordinary scheduler next considers the
        // one Reporting routine. It never sends from this request.
        if (enabled)
        {
            await routines.RunNowAsync(ReportingRoutine.RoutineName, target: null, cancellationToken);
        }

        return await StateAsync(
            installation.PrdbUserHash,
            reportFulfilments,
            reportConfirmedAssignments,
            cancellationToken);
    }

    private async Task<ReportingSettingsState> StateAsync(
        string? userHash,
        bool reportFulfilments,
        bool reportConfirmedAssignments,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userHash))
        {
            return new ReportingSettingsState(
                ReportFulfilments: reportFulfilments,
                FulfilmentBacklog: 0,
                ReportConfirmedAssignments: reportConfirmedAssignments,
                ConfirmedAssignmentBacklog: 0);
        }

        return new ReportingSettingsState(
            reportFulfilments,
            await fulfilments.CountAsync(userHash, cancellationToken),
            reportConfirmedAssignments,
            await context.ConfirmedAssignments.CountAsync(
                row => row.UserHash == userHash && row.SentAt == null,
                cancellationToken));
    }
}
