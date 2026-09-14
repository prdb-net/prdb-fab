using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Reporting;

/// <param name="PreviewPublicationExplained">
/// ADR 0064's gate, as the browser needs it: false until somebody has saved
/// this form, and nothing is generated or published while it is.
/// </param>
public sealed record ReportingSettingsState(
    bool ReportFulfilments,
    int FulfilmentBacklog,
    bool ReportConfirmedAssignments,
    int ConfirmedAssignmentBacklog,
    bool PublishGeneratedPreviews,
    bool PreviewPublicationExplained);

/// <summary>The three independent Reporting channels.</summary>
public sealed class ReportingSettings(
    FabDbContext context,
    FulfilmentDifference fulfilments,
    IRoutineStore routines,
    TimeProvider time)
{
    public async Task<ReportingSettingsState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation
            .Select(row => new
            {
                row.PrdbUserHash,
                row.ReportFulfilments,
                row.ReportConfirmedAssignments,
                row.PublishGeneratedPreviews,
                row.PreviewPublicationExplainedAt,
            })
            .SingleAsync(cancellationToken);

        return await StateAsync(
            installation.PrdbUserHash,
            installation.ReportFulfilments,
            installation.ReportConfirmedAssignments,
            installation.PublishGeneratedPreviews,
            installation.PreviewPublicationExplainedAt is not null,
            cancellationToken);
    }

    /// <summary>
    /// Stores all three switches, and records that the publication explanation
    /// has been in front of somebody.
    /// </summary>
    /// <remarks>
    /// ADR 0064 puts the stamp on the save rather than on the read: a page that
    /// marks itself explained by having been fetched would be explained by a
    /// browser prefetch, and a person who scrolled past it would have consented
    /// by arriving. Saving is the act, and the onboarding step and this route
    /// are the same act through two frames.
    /// </remarks>
    public async Task<ReportingSettingsState> SaveAsync(
        bool reportFulfilments,
        bool reportConfirmedAssignments,
        bool publishGeneratedPreviews,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation
            .AsTracking()
            .SingleAsync(cancellationToken);
        var enabled = (!installation.ReportFulfilments && reportFulfilments)
            || (!installation.ReportConfirmedAssignments && reportConfirmedAssignments);

        installation.ReportFulfilments = reportFulfilments;
        installation.ReportConfirmedAssignments = reportConfirmedAssignments;
        installation.PublishGeneratedPreviews = publishGeneratedPreviews;

        // Set once and then left alone. It says the explanation has been in
        // front of somebody, which does not become truer for being saved again,
        // and a moving stamp would lose the one thing worth reading off it.
        installation.PreviewPublicationExplainedAt ??= time.GetUtcNow();

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
            publishGeneratedPreviews,
            explained: true,
            cancellationToken);
    }

    private async Task<ReportingSettingsState> StateAsync(
        string? userHash,
        bool reportFulfilments,
        bool reportConfirmedAssignments,
        bool publishGeneratedPreviews,
        bool explained,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userHash))
        {
            return new ReportingSettingsState(
                ReportFulfilments: reportFulfilments,
                FulfilmentBacklog: 0,
                ReportConfirmedAssignments: reportConfirmedAssignments,
                ConfirmedAssignmentBacklog: 0,
                PublishGeneratedPreviews: publishGeneratedPreviews,
                PreviewPublicationExplained: explained);
        }

        return new ReportingSettingsState(
            reportFulfilments,
            await fulfilments.CountAsync(userHash, cancellationToken),
            reportConfirmedAssignments,
            await context.ConfirmedAssignments.CountAsync(
                row => row.UserHash == userHash && row.SentAt == null,
                cancellationToken),
            publishGeneratedPreviews,
            explained);
    }
}
