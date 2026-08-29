using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>
/// The deliberately small manual surface of release discovery. Making a
/// routine due remains one atomic schedule-row update; this type does not run one.
/// </summary>
public sealed class ReleaseDiscoveryControls(FabDbContext context, IRoutineStore routines)
{
    public async Task<IReadOnlyList<ReleaseDiscoveryRoutine>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var rows = await context.Routines
            .Where(row => row.Name == DiscoveryRoutineNames.WantedSweep
                || row.Name == DiscoveryRoutineNames.Screening
                || row.Name == DiscoveryRoutineNames.BackwardsSearch
                || row.Name == DiscoveryRoutineNames.Identification)
            .ToListAsync(cancellationToken);
        var indexers = await context.Indexers
            .Where(row => row.Enabled)
            .Select(row => new { row.Id, row.Name })
            .ToDictionaryAsync(row => row.Id.ToString("D"), row => row.Name, cancellationToken);

        var answer = new List<ReleaseDiscoveryRoutine>();
        answer.AddRange(rows
            .Where(row => row.Name == DiscoveryRoutineNames.WantedSweep
                && row.Target is not null
                && indexers.ContainsKey(row.Target))
            .OrderBy(row => indexers[row.Target!], StringComparer.OrdinalIgnoreCase)
            .Select(row => Describe(
                row,
                ReleaseDiscoveryRoutineKind.WantedSweep,
                $"Wanted Sweep — {indexers[row.Target!]}",
                "Search this Indexer for the least recently searched Wanted Videos.")));

        AddGlobal(
            answer,
            rows,
            DiscoveryRoutineNames.Screening,
            ReleaseDiscoveryRoutineKind.Screening,
            "Screening",
            "Screen newly cached Releases against the local Catalogue.");
        AddGlobal(
            answer,
            rows,
            DiscoveryRoutineNames.BackwardsSearch,
            ReleaseDiscoveryRoutineKind.BackwardsScreening,
            "Backwards Screening",
            "Reconsider cached Releases when the Catalogue has learned new titles.");
        AddGlobal(
            answer,
            rows,
            DiscoveryRoutineNames.Identification,
            ReleaseDiscoveryRoutineKind.Identification,
            "Release Identification",
            "Ask prdb to identify Releases that passed Screening.");

        return answer;
    }

    public async Task<ReleaseDiscoveryRunNowVerdict> RunNowAsync(
        ReleaseDiscoveryRunNowRequest request,
        CancellationToken cancellationToken)
    {
        var (name, target) = request.Kind switch
        {
            ReleaseDiscoveryRoutineKind.WantedSweep when request.Target is not null =>
                (DiscoveryRoutineNames.WantedSweep, request.Target.Value.ToString("D")),
            ReleaseDiscoveryRoutineKind.Screening when request.Target is null =>
                (DiscoveryRoutineNames.Screening, null),
            ReleaseDiscoveryRoutineKind.BackwardsScreening when request.Target is null =>
                (DiscoveryRoutineNames.BackwardsSearch, null),
            ReleaseDiscoveryRoutineKind.Identification when request.Target is null =>
                (DiscoveryRoutineNames.Identification, null),
            _ => (null, null),
        };

        if (name is null)
        {
            return new(false, "That Release discovery routine is not available.");
        }

        if (request.Kind == ReleaseDiscoveryRoutineKind.WantedSweep
            && !await context.Indexers.AnyAsync(
                row => row.Id == request.Target && row.Enabled,
                cancellationToken))
        {
            return new(false, "That Indexer is not enabled.");
        }

        var verdict = await routines.RunNowDetailedAsync(name, target, cancellationToken);
        return new(verdict.Accepted, verdict.Detail);
    }

    private static void AddGlobal(
        ICollection<ReleaseDiscoveryRoutine> answer,
        IEnumerable<RoutineRow> rows,
        string name,
        ReleaseDiscoveryRoutineKind kind,
        string label,
        string detail)
    {
        var row = rows.SingleOrDefault(row => row.Name == name && row.Target is null);
        if (row is not null)
        {
            answer.Add(Describe(row, kind, label, detail));
        }
    }

    private static ReleaseDiscoveryRoutine Describe(
        RoutineRow row,
        ReleaseDiscoveryRoutineKind kind,
        string label,
        string detail) =>
        new(
            kind,
            row.Target is null ? null : Guid.Parse(row.Target),
            label,
            detail,
            row.DueAt,
            row.LastSuccessAt,
            row.LastFailureAt,
            row.ConsecutiveFailures);
}

public enum ReleaseDiscoveryRoutineKind
{
    WantedSweep,
    Screening,
    BackwardsScreening,
    Identification,
}

public sealed record ReleaseDiscoveryRoutine(
    ReleaseDiscoveryRoutineKind Kind,
    Guid? Target,
    string Label,
    string Detail,
    DateTimeOffset DueAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int ConsecutiveFailures);

public sealed record ReleaseDiscoveryRunNowRequest(
    ReleaseDiscoveryRoutineKind Kind,
    Guid? Target);

public sealed record ReleaseDiscoveryRunNowVerdict(bool Accepted, string Detail);
