using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Reporting;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Reporting;

/// <summary>
/// The one governed Reporting routine, draining one bounded batch from each
/// enabled channel per turn and recording remote state only after prdb's
/// per-entry answer arrives.
/// </summary>
public sealed class ReportingRoutine(
    FabDbContext context,
    FulfilmentDifference fulfilments,
    PrdbGateway prdb,
    TimeProvider time) : IRoutine
{
    public const string RoutineName = "Reporting";
    public const int FulfilmentBatchSize = 50;
    public const int ConfirmedAssignmentBatchSize = 200;

    private const int FulfilmentUpdated = 0;
    private const int FulfilmentUnchanged = 1;
    private const int FulfilmentNotWanted = 2;
    private const int FulfilmentNotFound = 3;
    private const int ConfirmedRecorded = 0;
    private const int ConfirmedUpdated = 1;
    private const int ConfirmedConflicted = 2;
    private const int ConfirmedVideoNotFound = 3;
    private const int UserConfirmed = 0;
    private const int OtherApplication = 3;

    public string Name => RoutineName;

    public Lane Lane => Lane.Sync;

    public TimeSpan Cadence => TimeSpan.FromMinutes(15);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var settings = await CurrentSettingsAsync(cancellationToken);
        if (!settings.CanReport)
        {
            return RunResult.NothingToDo;
        }

        var handled = 0;
        var notes = new List<string>();

        if (settings.ReportFulfilments)
        {
            var pending = await fulfilments.PendingAsync(
                settings.UserHash,
                FulfilmentBatchSize,
                cancellationToken);
            if (pending.Count > 0)
            {
                var result = await SendFulfilmentsAsync(settings, pending, cancellationToken);
                handled += result.ItemsHandled;
                AddNote(notes, result.Reason);
            }
        }

        if (settings.ReportConfirmedAssignments)
        {
            var assignments = await context.ConfirmedAssignments
                .AsTracking()
                .Where(row => row.UserHash == settings.UserHash && row.SentAt == null)
                .OrderBy(row => row.VideoId)
                .ThenBy(row => row.OsHash)
                .Take(ConfirmedAssignmentBatchSize)
                .ToListAsync(cancellationToken);
            if (assignments.Count > 0)
            {
                var result = await SendConfirmedAssignmentsAsync(settings, assignments, cancellationToken);
                handled += result.ItemsHandled;
                AddNote(notes, result.Reason);
            }
        }

        return handled == 0
            ? RunResult.NothingToDo
            : notes.Count == 0
                ? RunResult.Handled(handled)
                : RunResult.Handled(handled, string.Join(" ", notes));
    }

    private async Task<RunResult> SendFulfilmentsAsync(
        CurrentReportingSettings settings,
        IReadOnlyList<PendingFulfilment> pending,
        CancellationToken cancellationToken)
    {
        if (!await StillEnabledAsync(settings, fulfilments: true, cancellationToken))
        {
            return RunResult.NothingToDo;
        }

        var request = new FulfillWantedVideosBatchRequest
        {
            Items = [.. pending.Select(item => new FulfillWantedVideoItem
            {
                VideoId = item.VideoId,
                IsFulfilled = item.IsFulfilled,
                FulfilledAtUtc = item.FulfilledAt,
                FulfilledInQuality = QualityForApi(item.Quality),
                FulfillmentByApp = item.IsFulfilled ? OtherApplication : null,
                FulfillmentExternalId = null,
            })],
        };
        var answer = await prdb.AskAsync(
            settings.ApiKey,
            PrdbWork.Writes,
            (client, token) => client.WantedVideos.Fulfillments.PostAsync(request, cancellationToken: token),
            cancellationToken)
            ?? throw new InvalidOperationException("prdb returned no Fulfilment results.");

        var results = answer.Results
            ?? throw new InvalidOperationException("prdb returned no Fulfilment results.");
        if (results.Count != pending.Count)
        {
            throw new InvalidOperationException(
                $"prdb returned {results.Count} Fulfilment results for {pending.Count} entries.");
        }

        var requestedIds = pending.Select(item => item.VideoId).ToHashSet();
        var resultIds = results.Select(result => result.VideoId).ToList();
        if (resultIds.Any(id => id is null)
            || resultIds.Select(id => id!.Value).ToHashSet().Count != results.Count
            || resultIds.Any(id => id is not { } value || !requestedIds.Contains(value))
            || results.Any(result => result.Outcome is not (
                FulfilmentUpdated
                or FulfilmentUnchanged
                or FulfilmentNotWanted
                or FulfilmentNotFound)))
        {
            throw new InvalidOperationException(
                "prdb returned invalid Fulfilment results.");
        }

        var reported = await context.ReportedStates
            .AsTracking()
            .Where(state => state.UserHash == settings.UserHash)
            .ToDictionaryAsync(state => state.VideoId, cancellationToken);
        var notes = new List<string>();
        foreach (var result in results)
        {
            var desired = pending.SingleOrDefault(item => item.VideoId == result.VideoId)
                ?? throw new InvalidOperationException("prdb returned a Fulfilment result that was not requested.");
            if (!reported.TryGetValue(desired.VideoId, out var row))
            {
                row = new ReportedStateRow { VideoId = desired.VideoId, UserHash = settings.UserHash };
                context.ReportedStates.Add(row);
                reported.Add(desired.VideoId, row);
            }

            row.IsFulfilled = desired.IsFulfilled;
            row.Quality = desired.Quality;
            row.FulfilledAt = desired.FulfilledAt;
            row.TerminalOutcome = result.Outcome switch
            {
                FulfilmentUpdated or FulfilmentUnchanged => null,
                FulfilmentNotWanted => ReportingOutcome.NotWanted,
                FulfilmentNotFound => ReportingOutcome.NotFound,
                _ => throw new InvalidOperationException($"prdb returned unknown Fulfilment outcome {result.Outcome}."),
            };

            if (row.TerminalOutcome is { } terminal)
            {
                notes.Add($"{desired.VideoId}: {terminal}");
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result(pending.Count, notes, "Fulfilment");
    }

    private async Task<RunResult> SendConfirmedAssignmentsAsync(
        CurrentReportingSettings settings,
        IReadOnlyList<ConfirmedAssignmentRow> assignments,
        CancellationToken cancellationToken)
    {
        if (!await StillEnabledAsync(settings, fulfilments: false, cancellationToken))
        {
            return RunResult.NothingToDo;
        }

        var request = new SubmitVideoFilehashesRequest
        {
            Items = [.. assignments.Select(item => new SubmitVideoFilehashItem
            {
                VideoId = item.VideoId,
                OsHash = item.OsHash,
                Filesize = item.SizeBytes,
                Source = UserConfirmed,
                DurationMs = item.RuntimeSeconds is { } seconds ? checked((long)seconds * 1000) : null,
                Width = item.Width,
                Height = item.Height,
                VideoCodec = item.VideoCodec,
                Filename = item.ArrivalFileName,
                ReleaseName = item.ReleaseName,
            })],
        };
        var answer = await prdb.AskAsync(
            settings.ApiKey,
            PrdbWork.Writes,
            (client, token) => client.Videos.FilehashSubmissions.PostAsync(request, cancellationToken: token),
            cancellationToken)
            ?? throw new InvalidOperationException("prdb returned no Confirmed Assignment results.");

        var results = answer.Results
            ?? throw new InvalidOperationException("prdb returned no Confirmed Assignment results.");
        if (results.Count != assignments.Count)
        {
            throw new InvalidOperationException(
                $"prdb returned {results.Count} Confirmed Assignment results for {assignments.Count} entries.");
        }

        var requestedKeys = assignments
            .Select(item => AssignmentKey(item.VideoId, item.OsHash))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resultKeys = results
            .Select(item => AssignmentKey(item.VideoId ?? Guid.Empty, item.OsHash ?? string.Empty))
            .ToList();
        if (results.Any(item => item.VideoId is null || string.IsNullOrWhiteSpace(item.OsHash))
            || resultKeys.ToHashSet(StringComparer.OrdinalIgnoreCase).Count != resultKeys.Count
            || resultKeys.Any(key => !requestedKeys.Contains(key))
            || results.Any(result => result.Outcome is not (
                ConfirmedRecorded
                or ConfirmedUpdated
                or ConfirmedConflicted
                or ConfirmedVideoNotFound)))
        {
            throw new InvalidOperationException(
                "prdb returned invalid Confirmed Assignment results.");
        }

        var sentAt = time.GetUtcNow();
        var notes = new List<string>();
        foreach (var result in results)
        {
            var assignment = assignments.SingleOrDefault(item =>
                item.VideoId == result.VideoId
                && string.Equals(item.OsHash, result.OsHash, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "prdb returned a Confirmed Assignment result that was not requested.");
            var outcome = result.Outcome switch
            {
                ConfirmedRecorded => "Recorded",
                ConfirmedUpdated => "Updated",
                ConfirmedConflicted => "Conflicted",
                ConfirmedVideoNotFound => "VideoNotFound",
                _ => throw new InvalidOperationException(
                    $"prdb returned unknown Confirmed Assignment outcome {result.Outcome}."),
            };

            assignment.PrdbAnswer = outcome;
            assignment.SentAt = sentAt;
            if (result.Outcome is ConfirmedConflicted or ConfirmedVideoNotFound)
            {
                notes.Add($"{assignment.OsHash}/{assignment.VideoId}: {outcome}");
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result(assignments.Count, notes, "Confirmed Assignment");
    }

    private async Task<bool> StillEnabledAsync(
        CurrentReportingSettings expected,
        bool fulfilments,
        CancellationToken cancellationToken)
    {
        var current = await CurrentSettingsAsync(cancellationToken);

        return current.ApiKey == expected.ApiKey
            && current.UserHash == expected.UserHash
            && (fulfilments ? current.ReportFulfilments : current.ReportConfirmedAssignments);
    }

    private async Task<CurrentReportingSettings> CurrentSettingsAsync(CancellationToken cancellationToken)
    {
        var installation = await context.Installation
            .Select(row => new
            {
                row.PrdbApiKey,
                row.PrdbUserHash,
                row.ReportFulfilments,
                row.ReportConfirmedAssignments,
            })
            .SingleAsync(cancellationToken);

        return new CurrentReportingSettings(
            installation.PrdbApiKey ?? string.Empty,
            installation.PrdbUserHash ?? string.Empty,
            installation.ReportFulfilments,
            installation.ReportConfirmedAssignments);
    }

    private static int? QualityForApi(FulfilmentQuality? quality) => quality switch
    {
        FulfilmentQuality.P720 => 0,
        FulfilmentQuality.P1080 => 1,
        FulfilmentQuality.P2160 => 2,
        null => null,
        _ => throw new ArgumentOutOfRangeException(nameof(quality)),
    };

    private static RunResult Result(int count, IReadOnlyList<string> notes, string channel) =>
        notes.Count == 0
            ? RunResult.Handled(count)
            : RunResult.Handled(
                count,
                $"prdb terminally disagreed with {notes.Count} {channel} report(s): {string.Join(", ", notes)}.");

    private static string AssignmentKey(Guid videoId, string osHash) => $"{videoId:N}/{osHash}";

    private static void AddNote(ICollection<string> notes, string? note)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            notes.Add(note);
        }
    }

    private sealed record CurrentReportingSettings(
        string ApiKey,
        string UserHash,
        bool ReportFulfilments,
        bool ReportConfirmedAssignments)
    {
        public bool CanReport =>
            !string.IsNullOrWhiteSpace(ApiKey)
            && !string.IsNullOrWhiteSpace(UserHash)
            && (ReportFulfilments || ReportConfirmedAssignments);
    }
}
