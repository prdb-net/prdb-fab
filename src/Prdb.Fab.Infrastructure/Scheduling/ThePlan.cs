using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Scheduling;

/// <summary>Adapt recurring polls to discovered limits; never slow a work-set continuation.</summary>
public sealed class ThePlan(
    FabDbContext context,
    PrdbGovernor governor,
    TimeProvider time,
    ILogger<ThePlan> logger)
{
    private bool recorded;

    public bool Shedding => governor.Snapshot() is { Hourly: { } hour, Monthly: { } month }
        ? IdleProfile.CadenceMultiplier(hour.Limit, month.Limit, governor.WantedCadence) > 1 : recorded;

    public bool CarriesTheSchedule => !Shedding;

    public TimeSpan CadenceFor(IRoutine routine) =>
        routine is ISpendsPrdbBudget and not IOneShot
            && (routine is not IWorkSetPaced || routine is UserPreviewFeedRoutine)
            ? IdleProfile.Adapt(routine.Cadence, governor.Snapshot().Hourly?.Limit, governor.Snapshot().Monthly?.Limit, governor.WantedCadence)
            : routine.Cadence;

    public async Task NoteAsync(CancellationToken cancellationToken)
    {
        var since = await context.Installation
            .Select(row => row.PlanShortSince)
            .SingleOrDefaultAsync(cancellationToken);

        recorded = since is not null;

        if (governor.LastReading is not { } reading)
        {
            // No fresh account evidence: keep the last recorded adaptation.
            return;
        }

        var carried = IdleProfile.CadenceMultiplier(reading.Limit, governor.Snapshot().Monthly?.Limit, governor.WantedCadence) <= 1;

        if (carried == !recorded)
        {
            return;
        }

        recorded = !carried;

        if (carried)
        {
            logger.LogInformation(
                "The prdb plan carries the schedule again. Nothing is being shed.");

            await WriteAsync(null, cancellationToken);
            return;
        }

        // ADR 0043: the sentence says what was given up as well as what
        // happened, because the person reading it is being told their tool is
        // deliberately slower than it was designed to be.
        logger.LogInformation(
            "The prdb plan allows {HourlyLimit} hourly and {MonthlyLimit} rolling monthly requests; "
            + "the ordinary schedule needs {HourlyDemand:0.#}/hour and {MonthlyDemand:0}/31 days. "
            + "Recurring polls are automatically spaced to fit the plan with 20% account reserve.",
            governor.LastReading?.Limit,
            governor.Snapshot().Monthly?.Limit,
            IdleProfile.ScheduledRequestsAnHourFor(governor.WantedCadence),
            IdleProfile.ScheduledRequestsPerWindowFor(governor.WantedCadence));

        await WriteAsync(time.GetUtcNow(), cancellationToken);
    }

    private Task WriteAsync(DateTimeOffset? since, CancellationToken cancellationToken) =>
        context.Installation.ExecuteUpdateAsync(
            row => row.SetProperty(installation => installation.PlanShortSince, since),
            cancellationToken);
}
