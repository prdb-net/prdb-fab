using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Reporting;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Status;

/// <summary>ADR 0018's local, derived answer to “is anything broken?”.</summary>
public sealed class StatusService(
    FabDbContext context,
    PrdbGovernor governor,
    ReportingSettings reporting,
    CataloguePins cataloguePins,
    IRoutineStore routineStore,
    TimeProvider time)
{
    private const string BeforeDownloadGateName = "BeforeDownload";
    private static readonly string[] StageOrder = ["sync-prdb", "sync-indexers", "match", "decide", "download", "file"];

    public async Task<StatusState> ReadAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var sevenDaysAgo = now.AddDays(-7);
        var routines = await context.Routines.OrderBy(row => row.Name).ThenBy(row => row.Target).ToListAsync(cancellationToken);
        var routineIds = routines.Select(row => row.Id).ToArray();
        var runs = await context.RoutineRuns
            .Where(row => routineIds.Contains(row.RoutineId))
            .OrderBy(row => row.StartedAt)
            .ToListAsync(cancellationToken);
        var indexers = await context.Indexers.OrderBy(row => row.Rank).ThenBy(row => row.Name).ToListAsync(cancellationToken);
        var walkStates = await context.IndexerWalkStates.ToListAsync(cancellationToken);
        var installation = await context.Installation.SingleAsync(cancellationToken);
        var reportingState = await reporting.ReadAsync(cancellationToken);

        var workSets = await WorkSetsAsync(routines, reportingState, cancellationToken);
        var routineFacts = routines.Select(row => DescribeRoutine(
            row,
            indexers,
            runs.Where(run => run.RoutineId == row.Id).ToArray(),
            workSets.GetValueOrDefault(Key(row.Name, row.Target)))).ToArray();

        var conditions = new List<StatusCondition>();
        AddInstallationGaps(conditions, installation, governor);
        AddRoutineGaps(conditions, routines, runs, indexers);
        AddDeferralBrakes(conditions, routines, now);
        AddIndexerBudgetBrakes(conditions, indexers, walkStates, now);
        AddReportingBrakes(conditions, reportingState);

        var admissions = await context.GateAdmissions.ToListAsync(cancellationToken);
        var gateTallies = await GateTalliesAsync(sevenDaysAgo, admissions, cancellationToken);
        var notDownloadedReasons = await context.ReleasesNotDownloaded
            .Where(row => row.At >= sevenDaysAgo)
            .Select(row => row.Reason)
            .ToListAsync(cancellationToken);
        var notDownloaded = notDownloadedReasons
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .Select(group => new StatusNamedCount(group.Key, group.Count()))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        AddGateBrakes(conditions, gateTallies, admissions);

        var downloads = await context.Downloads.ToListAsync(cancellationToken);
        var review = await context.ArrivingFiles.Where(row => row.Reason != null).ToListAsync(cancellationToken);
        var filing = await context.ArrivingFiles
            .Where(row => row.State == ArrivingFileState.Filing)
            .OrderBy(row => row.LastAttemptedAt)
            .ToListAsync(cancellationToken);
        AddDownloadBrakes(conditions, downloads, review, installation.RetryBudget);

        var stages = StageOrder.Select(id => BuildStage(
            id,
            routineFacts,
            conditions,
            indexers,
            walkStates,
            gateTallies,
            notDownloaded,
            downloads,
            review,
            filing,
            installation,
            governor,
            now)).ToArray();

        return new StatusState(
            conditions.Count(condition => condition.Kind == StatusConditionKind.Gap && !condition.Cleared),
            await LastUsefulActAsync(cancellationToken),
            stages,
            [new StatusLink("Downloads", "/downloads"), new StatusLink("Review Queue", "/review-queue"), new StatusLink("Operation Log", "/operation-log")]);
    }

    public async Task<RunNowVerdict> RunNowAsync(
        StatusRunNowRequest request,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Routines.AsTracking().SingleOrDefaultAsync(
            item => item.Name == request.Name && item.Target == request.Target,
            cancellationToken);
        if (row is null)
        {
            return new RunNowVerdict(RunNowOutcome.Refused, "There is no schedule row for that routine.");
        }

        var now = time.GetUtcNow();
        if (UsesPrdb(request.Name) && governor.RefusedWith is not null)
        {
            return await NoteAsync(row, RunNowOutcome.Refused,
                "prdb has permanently refused the configured key. Repair the connection first.", now, cancellationToken);
        }

        if (UsesIndexerQueryBudget(request.Name) && Guid.TryParse(request.Target, out var indexerId))
        {
            var budget = await (from indexer in context.Indexers
                                join state in context.IndexerWalkStates on indexer.Id equals state.IndexerId
                                where indexer.Id == indexerId
                                select new { indexer.DailyQueryBudget, state.QueryDay, state.QueriesSpentToday })
                .SingleOrDefaultAsync(cancellationToken);
            if (budget is not null
                && budget.QueryDay.UtcDateTime.Date == now.UtcDateTime.Date
                && budget.QueriesSpentToday >= budget.DailyQueryBudget)
            {
                return await NoteAsync(row, RunNowOutcome.Deferred,
                    "The Indexer's daily query budget is spent. Run now cannot override it.", now, cancellationToken);
            }
        }

        var reportingState = await reporting.ReadAsync(cancellationToken);
        if (request.Name == ReportingRoutine.RoutineName
            && !reportingState.ReportFulfilments
            && !reportingState.ReportConfirmedAssignments)
        {
            return await NoteAsync(row, RunNowOutcome.Refused,
                "Both Reporting channels are switched off. Run now cannot override that choice.", now, cancellationToken);
        }

        var workSets = await WorkSetsAsync([row], reportingState, cancellationToken);
        if (EmptyWorkSetRefusesRunNow(request.Name)
            && workSets.GetValueOrDefault(Key(row.Name, row.Target)) is 0)
        {
            return await NoteAsync(row, RunNowOutcome.Refused,
                "The routine's work set is empty. Refreshing would do no work.", now, cancellationToken);
        }

        return await routineStore.RunNowDetailedAsync(request.Name, request.Target, cancellationToken);
    }

    private async Task<RunNowVerdict> NoteAsync(
        RoutineRow row,
        RunNowOutcome outcome,
        string detail,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        row.LastRunNowAt = now;
        row.LastRunNowOutcome = outcome;
        row.LastRunNowDetail = detail;
        row.RunNowPending = false;
        await context.SaveChangesAsync(cancellationToken);
        return new RunNowVerdict(outcome, detail);
    }

    private async Task<Dictionary<string, int?>> WorkSetsAsync(
        IReadOnlyCollection<RoutineRow> routines,
        ReportingSettingsState reportingState,
        CancellationToken cancellationToken)
    {
        var answer = routines.ToDictionary(row => Key(row.Name, row.Target), _ => (int?)null);
        async Task SetAsync(string name, Func<Task<int>> count)
        {
            var keys = answer.Keys.Where(key => key.StartsWith(name + "\0", StringComparison.Ordinal)).ToArray();
            if (keys.Length == 0) return;
            var value = await count();
            foreach (var key in keys)
            {
                answer[key] = value;
            }
        }

        await SetAsync(DiscoveryRoutineNames.Screening,
            () => context.Releases.CountAsync(row => row.IdentificationState == IdentificationState.Unexamined, cancellationToken));
        await SetAsync(DiscoveryRoutineNames.BackwardsSearch, async () =>
            await context.CatalogueVideos.CountAsync(row => !row.TitleSearchedBackwards, cancellationToken)
            + await context.CatalogueVideoPreNames.CountAsync(row => !row.SearchedBackwards, cancellationToken));
        await SetAsync(DiscoveryRoutineNames.Identification,
            () => context.Releases.CountAsync(row => row.IdentificationState == IdentificationState.Awaiting, cancellationToken));
        await SetAsync(ArrivalIdentificationRoutine.RoutineName,
            () => context.ArrivingFiles.CountAsync(row => row.State == ArrivingFileState.AwaitingIdentification && row.Reason == null, cancellationToken));
        await SetAsync(FilingRoutine.RoutineName,
            () => context.ArrivingFiles.CountAsync(row => row.State == ArrivingFileState.AwaitingFiling, cancellationToken));
        await SetAsync(CollectingRoutine.RoutineName,
            () => context.Downloads.CountAsync(row => row.State == DownloadState.Completed, cancellationToken));
        await SetAsync(TidyUpRoutine.RoutineName,
            () => context.Downloads.CountAsync(row => row.State == DownloadState.Collected && row.TidiedAt == null, cancellationToken));
        await SetAsync(DownloadFollowingRoutine.RoutineName, async () =>
        {
            var outstanding = await context.Downloads.CountAsync(
                row => row.State == DownloadState.Outstanding,
                cancellationToken);
            var retryBudget = await context.Installation
                .Select(row => row.RetryBudget)
                .SingleAsync(cancellationToken);
            var retryable = await context.Downloads
                .GroupBy(row => row.VideoId)
                .CountAsync(group => group.Count() < retryBudget
                    && group.All(row => row.State == DownloadState.Failed), cancellationToken);
            return outstanding + retryable;
        });
        await SetAsync(ReportingRoutine.RoutineName,
            () => Task.FromResult(reportingState.FulfilmentBacklog + reportingState.ConfirmedAssignmentBacklog));
        await SetAsync(DiscoveryRoutineNames.WantedSweep, async () =>
            (await context.WantedVideos
                .Select(row => row.Video!.Title)
                .ToListAsync(cancellationToken))
            .Count(title => WantedSearchTitle.IsSearchable(WantedSearchTitle.Of(title))));
        await SetAsync(CatalogueRepairRoutine.RoutineName,
            () => cataloguePins.Pinned(context.CatalogueVideos).CountAsync(cancellationToken));
        await SetAsync(ArtworkRoutine.RoutineName, () =>
        {
            var pendingImages = ChosenImages.In(
                context,
                context.CatalogueImages.Where(image => !image.Cached && !image.FoundDead));
            return cataloguePins.Pinned(context.CatalogueVideos)
                .CountAsync(video => pendingImages.Any(image => image.VideoId == video.Id), cancellationToken);
        });
        return answer;
    }

    private static StatusRoutine DescribeRoutine(
        RoutineRow row,
        IReadOnlyList<IndexerRow> indexers,
        IReadOnlyList<RoutineRunRow> runs,
        int? workSetSize)
    {
        var indexer = row.Target is not null && Guid.TryParse(row.Target, out var id)
            ? indexers.SingleOrDefault(item => item.Id == id)
            : null;
        var last = runs.LastOrDefault();
        var lastCompleted = runs.LastOrDefault(run => run.Outcome == RunOutcome.Succeeded && run.ItemsHandled > 0);
        return new StatusRoutine(
            row.Name,
            row.Target,
            indexer is null ? Label(row.Name) : $"{Label(row.Name)} — {indexer.Name}",
            StageOf(row.Name),
            row.DueAt,
            row.LastSuccessAt,
            row.LastFailureAt,
            row.ConsecutiveFailures,
            row.ConsecutiveFailures is > 0 and < 3,
            workSetSize,
            lastCompleted?.FinishedAt,
            last?.ResultsSeen,
            last?.RowsAdded,
            row.LastRunNowAt,
            row.LastRunNowOutcome,
            row.LastRunNowDetail,
            row.RunNowPending);
    }

    private static void AddInstallationGaps(
        ICollection<StatusCondition> conditions,
        InstallationRow installation,
        PrdbGovernor governor)
    {
        if (installation.SabnzbdSkipped)
            conditions.Add(Gap("SABnzbd is not configured", "Downloads cannot start until this connection is configured.", "download", "/settings/connections/sabnzbd"));
        if (installation.IndexersSkipped)
            conditions.Add(Gap("No Indexer is configured", "Release discovery has nowhere to search.", "sync-indexers", "/settings/connections"));
        if (installation.PlanShortSince is { } since)
            conditions.Add(Gap("The prdb plan cannot carry the schedule", $"Lower-priority sync work has been reduced since {since:u}.", "sync-prdb", "/settings/connections/prdb"));
        if (governor.RefusedWith is { } status)
            conditions.Add(Gap("prdb refused the API key", $"prdb returned {status}; unattended requests remain stopped until a key works.", "sync-prdb", "/settings/connections/prdb"));
    }

    private static void AddRoutineGaps(
        ICollection<StatusCondition> conditions,
        IReadOnlyList<RoutineRow> routines,
        IReadOnlyList<RoutineRunRow> runs,
        IReadOnlyList<IndexerRow> indexers)
    {
        var indexerRoutines = routines.Where(row => row.Target is not null && Guid.TryParse(row.Target, out _)).ToArray();
        foreach (var group in indexerRoutines.GroupBy(row => row.Target!))
        {
            var indexer = indexers.SingleOrDefault(item => item.Id.ToString("D") == group.Key);
            var current = group.Where(IsCurrentGap).ToArray();
            var historical = group.Where(row => HadGap(row, runs)).ToArray();
            if (current.Length > 0 || historical.Length > 0)
            {
                conditions.Add(new StatusCondition(
                    StatusConditionKind.Gap,
                    current.Length > 0 ? $"{indexer?.Name ?? "Indexer"} routines are failing" : $"{indexer?.Name ?? "Indexer"} routines recovered",
                    $"Affected routines: {string.Join(", ", (current.Length > 0 ? current : historical).Select(row => Label(row.Name)).Distinct())}.",
                    "sync-indexers",
                    $"/settings/connections/indexers/{group.Key}",
                    current.Length == 0));
            }
        }

        foreach (var row in routines.Except(indexerRoutines))
        {
            var current = IsCurrentGap(row);
            if (!current && !HadGap(row, runs)) continue;
            conditions.Add(new StatusCondition(
                StatusConditionKind.Gap,
                current ? $"{Label(row.Name)} is failing" : $"{Label(row.Name)} recovered",
                current
                    ? $"The routine has failed {row.ConsecutiveFailures} consecutive times. {runs.Where(run => run.RoutineId == row.Id && run.Outcome == RunOutcome.Failed).OrderByDescending(run => run.StartedAt).Select(run => run.Reason).FirstOrDefault()}"
                    : "A qualifying failure remains in the retained run history; the latest evidence is healthy.",
                StageOf(row.Name),
                OwnerRoute(row.Name, row.Target),
                !current));
        }
    }

    private static bool IsCurrentGap(RoutineRow row) =>
        row.Name is SabnzbdRoutine.RoutineName or DownloadFollowingRoutine.RoutineName
            ? row.ConsecutiveFailures >= 1
            : row.ConsecutiveFailures >= 3;

    private static bool HadGap(RoutineRow row, IReadOnlyList<RoutineRunRow> allRuns)
    {
        var threshold = row.Name is SabnzbdRoutine.RoutineName or DownloadFollowingRoutine.RoutineName ? 1 : 3;
        var streak = 0;
        foreach (var run in allRuns.Where(run => run.RoutineId == row.Id).OrderBy(run => run.StartedAt))
        {
            streak = run.Outcome == RunOutcome.Failed ? streak + 1 : run.Outcome == RunOutcome.Succeeded ? 0 : streak;
            if (streak >= threshold) return true;
        }
        return false;
    }

    private static void AddDeferralBrakes(ICollection<StatusCondition> conditions, IEnumerable<RoutineRow> routines, DateTimeOffset now)
    {
        foreach (var row in routines.Where(row => row.DeferredUntil > now))
        {
            conditions.Add(Brake(
                $"{Label(row.Name)} is deliberately waiting",
                $"{row.LastDeferredReason} It may be due again at {row.DeferredUntil:u}.",
                StageOf(row.Name),
                OwnerRoute(row.Name, row.Target)));
        }
    }

    private static void AddIndexerBudgetBrakes(
        ICollection<StatusCondition> conditions,
        IEnumerable<IndexerRow> indexers,
        IEnumerable<IndexerWalkStateRow> states,
        DateTimeOffset now)
    {
        foreach (var indexer in indexers.Where(row => row.Enabled))
        {
            var state = states.SingleOrDefault(row => row.IndexerId == indexer.Id);
            var spent = state is not null && state.QueryDay.UtcDateTime.Date == now.UtcDateTime.Date
                ? state.QueriesSpentToday : 0;
            if (spent >= indexer.DailyQueryBudget)
            {
                conditions.Add(Brake(
                    $"{indexer.Name}'s daily query budget is spent",
                    $"{spent} of {indexer.DailyQueryBudget} queries have been used today. Searching resumes on the next UTC day.",
                    "sync-indexers",
                    $"/settings/connections/indexers/{indexer.Id:D}"));
            }
        }
    }

    private static void AddReportingBrakes(ICollection<StatusCondition> conditions, ReportingSettingsState state)
    {
        if (!state.ReportFulfilments && state.FulfilmentBacklog > 0)
            conditions.Add(Brake("Fulfilment reporting is off", $"{state.FulfilmentBacklog} local differences are intentionally not sent.", "file", "/settings/reporting"));
        if (!state.ReportConfirmedAssignments && state.ConfirmedAssignmentBacklog > 0)
            conditions.Add(Brake("Confirmed-assignment reporting is off", $"{state.ConfirmedAssignmentBacklog} human-confirmed assignments are intentionally not sent.", "file", "/settings/reporting"));
    }

    private static void AddGateBrakes(
        ICollection<StatusCondition> conditions,
        IReadOnlyList<StatusGateTally> tallies,
        IReadOnlyList<GateAdmissionRow> admissions)
    {
        foreach (var gate in tallies)
        {
            // The before-download gate is the shape the next Automation slice
            // will activate. Until then there are no automatic decisions and
            // therefore no deliberate hold to call a Brake.
            if (gate.Gate == BeforeDownloadGateName) continue;
            var allowed = admissions.Where(row => row.Gate == gate.Gate).Select(row => row.Confidence.ToString()).ToHashSet();
            var admitted = gate.Outcomes.Where(item => allowed.Contains(item.Name)).Sum(item => item.Count);
            if (gate.Total > 0 && admitted == 0)
            {
                conditions.Add(Brake(
                    "The after-download gate admitted nothing",
                    $"All {gate.Total} named outcomes in the last seven days were outside the configured set.",
                    "file",
                    "/settings/identification"));
            }
        }
    }

    private static void AddDownloadBrakes(
        ICollection<StatusCondition> conditions,
        IReadOnlyList<DownloadRow> downloads,
        IReadOnlyList<ArrivingFileRow> review,
        int retryBudget)
    {
        foreach (var group in downloads.GroupBy(row => row.VideoId)
                     .Where(group => group.Count() >= retryBudget && group.All(row => row.State == DownloadState.Failed)))
        {
            conditions.Add(Brake(
                "A Video's retry budget is spent",
                $"{group.Count()} of {retryBudget} Download attempts have been recorded. A person must choose what happens next.",
                "download",
                $"/releases?video={group.Key:D}&from=/status"));
        }

        foreach (var group in review.GroupBy(row => row.DownloadId))
        {
            conditions.Add(Brake(
                "An arriving Download needs a decision",
                $"{group.Count()} file(s) are held in the Review Queue; automation is deliberately stopped for them.",
                "decide",
                $"/review-queue?download={group.Key:D}"));
        }
    }

    private async Task<IReadOnlyList<StatusGateTally>> GateTalliesAsync(
        DateTimeOffset since,
        IReadOnlyList<GateAdmissionRow> admissions,
        CancellationToken cancellationToken)
    {
        var observed = await context.IdentificationOutcomes.Where(row => row.At >= since).ToListAsync(cancellationToken);
        var names = Enum.GetNames<IdentificationConfidence>().Concat(["SiteOnly", "Unknown"]).Distinct().ToArray();
        return new[] { BeforeDownloadGateName, AfterDownloadGate.Name }.Select(gate =>
        {
            var rows = observed.Where(row => row.Gate == gate).ToArray();
            var allowed = gate == BeforeDownloadGateName
                ? new HashSet<string>(["Exact", "Strong", "Probable"], StringComparer.Ordinal)
                : admissions.Where(row => row.Gate == gate).Select(row => row.Confidence.ToString()).ToHashSet(StringComparer.Ordinal);
            return new StatusGateTally(gate, rows.Length,
                names.Select(name => new StatusNamedCount(name, rows.Count(row => row.Outcome == name), allowed.Contains(name))).ToArray());
        }).ToArray();
    }

    private static StatusStage BuildStage(
        string id,
        IReadOnlyList<StatusRoutine> routines,
        IReadOnlyList<StatusCondition> conditions,
        IReadOnlyList<IndexerRow> indexers,
        IReadOnlyList<IndexerWalkStateRow> walkStates,
        IReadOnlyList<StatusGateTally> gateTallies,
        IReadOnlyList<StatusNamedCount> notDownloaded,
        IReadOnlyList<DownloadRow> downloads,
        IReadOnlyList<ArrivingFileRow> review,
        IReadOnlyList<ArrivingFileRow> filing,
        InstallationRow installation,
        PrdbGovernor governor,
        DateTimeOffset now)
    {
        var facts = new List<StatusFact>();
        if (id == "sync-prdb")
        {
            var budget = governor.LastReading;
            facts.Add(new("prdb connection", string.IsNullOrWhiteSpace(installation.PrdbApiKey) ? "Not configured" : governor.RefusedWith is null ? "Configured" : $"Refused with {governor.RefusedWith}", "/settings/connections/prdb"));
            facts.Add(new("Hourly budget", budget is null ? "Not read yet" : $"{budget.Remaining} of {budget.Limit} remaining", null));
        }
        if (id == "sync-indexers")
        {
            foreach (var indexer in indexers.Where(row => row.Enabled))
            {
                var state = walkStates.SingleOrDefault(row => row.IndexerId == indexer.Id);
                var spent = state is not null && state.QueryDay.UtcDateTime.Date == now.UtcDateTime.Date
                    ? state.QueriesSpentToday
                    : 0;
                facts.Add(new(indexer.Name, $"{indexer.LastVerdict} at {indexer.LastCheckedAt:u}; {spent} of {indexer.DailyQueryBudget} queries used", $"/settings/connections/indexers/{indexer.Id:D}"));
            }
        }
        if (id is "decide" or "file")
        {
            var gate = gateTallies.Single(item => item.Gate == (id == "decide" ? BeforeDownloadGateName : AfterDownloadGate.Name));
            facts.Add(new($"{gate.Gate} outcomes (7 days)", gate.Total == 0 ? "No named outcomes were observed." : string.Join(", ", gate.Outcomes.Select(item => $"{item.Name}{(item.Admitted ? " ✓" : string.Empty)} {item.Count}")), id == "file" ? "/settings/identification" : null));
        }
        if (id == "decide")
        {
            facts.Add(new("Automatic decisions", "None. Automation Rules are not available in this version.", null));
            facts.Add(new(
                "Releases not downloaded (7 days)",
                notDownloaded.Count == 0
                    ? "No Release exclusion was observed."
                    : string.Join(", ", notDownloaded.Select(item => $"{item.Name} {item.Count}")),
                "/downloads"));
        }
        if (id == "download")
        {
            facts.Add(new("SABnzbd connection", installation.SabnzbdUrl is null ? "Not configured" : "Configured", "/settings/connections/sabnzbd"));
            var outstanding = downloads.Where(row => row.State == DownloadState.Outstanding).ToArray();
            var oldest = outstanding.OrderBy(row => row.OutstandingSince).FirstOrDefault();
            facts.Add(new("Outstanding", oldest is null ? "No Downloads are outstanding." : $"{outstanding.Length} Download(s); oldest since {oldest.OutstandingSince:u}; SABnzbd last said {oldest.LastSabnzbdStatus ?? "nothing yet"}.", "/downloads"));
            facts.Add(new("Retry budget", installation.RetryBudget.ToString(), null));
        }
        if (id == "file")
        {
            facts.Add(new("Review Queue", $"{review.Count} open", "/review-queue"));
            var active = filing.FirstOrDefault();
            facts.Add(new(
                "Filing now",
                active is null
                    ? "No file is being moved."
                    : $"{active.IntendedPath ?? active.ArrivedName}, since {active.LastAttemptedAt:u}",
                null));
        }

        return new StatusStage(
            id,
            id switch { "sync-prdb" => "Sync (prdb)", "sync-indexers" => "Sync (Indexers)", "match" => "Match", "decide" => "Decide", "download" => "Download", _ => "File" },
            routines.Where(routine => routine.Stage == id).ToArray(),
            facts,
            conditions.Where(condition => condition.Stage == id && condition.Kind == StatusConditionKind.Gap).ToArray(),
            conditions.Where(condition => condition.Stage == id && condition.Kind == StatusConditionKind.Brake).ToArray());
    }

    private async Task<StatusUsefulAct?> LastUsefulActAsync(CancellationToken cancellationToken)
    {
        var filed = await context.OperationLogEntries
            .Where(row => row.Act == "Filed")
            .OrderByDescending(row => row.At)
            .Select(row => (DateTimeOffset?)row.At)
            .FirstOrDefaultAsync(cancellationToken);
        var downloaded = await context.Downloads.OrderByDescending(row => row.CreatedAt).Select(row => (DateTimeOffset?)row.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var cached = await context.Releases.OrderByDescending(row => row.FirstSeenAt).Select(row => (DateTimeOffset?)row.FirstSeenAt).FirstOrDefaultAsync(cancellationToken);
        return new[] { new StatusUsefulAct("Filed a library file", filed), new StatusUsefulAct("Started a Download", downloaded), new StatusUsefulAct("Added a Release to the cache", cached) }
            .Where(item => item.At is not null).OrderByDescending(item => item.At).FirstOrDefault();
    }

    private static StatusCondition Gap(string title, string detail, string stage, string? route) => new(StatusConditionKind.Gap, title, detail, stage, route, false);
    private static StatusCondition Brake(string title, string detail, string stage, string? route) => new(StatusConditionKind.Brake, title, detail, stage, route, false);
    private static string Key(string name, string? target) => name + "\0" + target;
    private static bool UsesPrdb(string name) => name.StartsWith("prdb.", StringComparison.Ordinal) || name is DiscoveryRoutineNames.Identification or ArrivalIdentificationRoutine.RoutineName or ReportingRoutine.RoutineName;
    private static bool UsesIndexerQueryBudget(string name) => name is DiscoveryRoutineNames.Walk or DiscoveryRoutineNames.Bootstrap or DiscoveryRoutineNames.CatchUp or DiscoveryRoutineNames.WantedSweep;
    private static bool EmptyWorkSetRefusesRunNow(string name) => name is
        DiscoveryRoutineNames.Screening
        or DiscoveryRoutineNames.BackwardsSearch
        or DiscoveryRoutineNames.Identification
        or DiscoveryRoutineNames.WantedSweep
        or ArrivalIdentificationRoutine.RoutineName
        or FilingRoutine.RoutineName
        or CollectingRoutine.RoutineName
        or TidyUpRoutine.RoutineName
        or DownloadFollowingRoutine.RoutineName
        or ReportingRoutine.RoutineName
        or CatalogueRepairRoutine.RoutineName;

    private static string StageOf(string name) => name switch
    {
        var value when value.StartsWith("prdb.", StringComparison.Ordinal) => "sync-prdb",
        DiscoveryRoutineNames.Caps or DiscoveryRoutineNames.Walk or DiscoveryRoutineNames.Bootstrap or DiscoveryRoutineNames.CatchUp or DiscoveryRoutineNames.WantedSweep => "sync-indexers",
        DiscoveryRoutineNames.Screening or DiscoveryRoutineNames.BackwardsSearch or DiscoveryRoutineNames.Identification or ArrivalIdentificationRoutine.RoutineName => "match",
        SabnzbdRoutine.RoutineName or DownloadFollowingRoutine.RoutineName => "download",
        CollectingRoutine.RoutineName or FilingRoutine.RoutineName or TidyUpRoutine.RoutineName or ReportingRoutine.RoutineName => "file",
        _ => "decide",
    };

    private static string Label(string name) => name switch
    {
        DiscoveryRoutineNames.Caps => "Indexer capabilities",
        DiscoveryRoutineNames.Walk => "Indexer walk",
        DiscoveryRoutineNames.Bootstrap => "Indexer bootstrap",
        DiscoveryRoutineNames.CatchUp => "Indexer catch-up",
        DiscoveryRoutineNames.WantedSweep => "Wanted sweep",
        DiscoveryRoutineNames.Screening => "Release screening",
        DiscoveryRoutineNames.BackwardsSearch => "Backwards screening",
        DiscoveryRoutineNames.Identification => "Release identification",
        _ => name,
    };

    private static string? OwnerRoute(string name, string? target) => name switch
    {
        var value when UsesPrdb(value) => "/settings/connections/prdb",
        DiscoveryRoutineNames.Caps or DiscoveryRoutineNames.Walk or DiscoveryRoutineNames.Bootstrap or DiscoveryRoutineNames.CatchUp or DiscoveryRoutineNames.WantedSweep when target is not null => $"/settings/connections/indexers/{target}",
        SabnzbdRoutine.RoutineName or DownloadFollowingRoutine.RoutineName or CollectingRoutine.RoutineName => "/settings/connections/sabnzbd",
        _ => null,
    };
}

public enum StatusConditionKind { Gap, Brake }
public sealed record StatusState(int GapCount, StatusUsefulAct? LastUsefulAct, IReadOnlyList<StatusStage> Stages, IReadOnlyList<StatusLink> Related);
public sealed record StatusUsefulAct(string Act, DateTimeOffset? At);
public sealed record StatusLink(string Label, string Route);
public sealed record StatusStage(string Id, string Title, IReadOnlyList<StatusRoutine> Routines, IReadOnlyList<StatusFact> Facts, IReadOnlyList<StatusCondition> Gaps, IReadOnlyList<StatusCondition> Brakes);
public sealed record StatusFact(string Label, string Value, string? Route);
public sealed record StatusCondition(StatusConditionKind Kind, string Title, string Detail, string Stage, string? Route, bool Cleared);
public sealed record StatusRoutine(string Name, string? Target, string Label, string Stage, DateTimeOffset DueAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, int ConsecutiveFailures, bool BackingOff, int? WorkSetSize, DateTimeOffset? LastCompletedAt, int? ResultsSeen, int? RowsAdded, DateTimeOffset? LastRunNowAt, RunNowOutcome? LastRunNowOutcome, string? LastRunNowDetail, bool RunNowPending);
public sealed record StatusGateTally(string Gate, int Total, IReadOnlyList<StatusNamedCount> Outcomes);
public sealed record StatusNamedCount(string Name, int Count, bool Admitted = false);
public sealed record StatusRunNowRequest(string Name, string? Target);
