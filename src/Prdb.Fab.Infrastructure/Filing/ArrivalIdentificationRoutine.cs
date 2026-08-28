using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Hashing;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Asks prdb to identify already-probed files without reopening them.</summary>
public sealed class ArrivalIdentificationRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    VideoDetails details,
    CatalogueRows catalogue,
    TimeProvider time,
    ILogger<ArrivalIdentificationRoutine> logger) : IRoutine
{
    public const string RoutineName = "Arrival identification";
    public const int BatchSize = 200;
    private static readonly TimeSpan OutcomeWindow = TimeSpan.FromDays(7);

    public string Name => RoutineName;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var arrivals = await context.ArrivingFiles
            .AsTracking()
            .Where(row => row.State == ArrivingFileState.AwaitingIdentification && row.Reason == null)
            .OrderBy(row => row.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (arrivals.Count == 0)
        {
            return RunResult.NothingToDo;
        }

        var apiKey = await context.Installation
            .Select(row => row.PrdbApiKey)
            .SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return RunResult.NothingToDo;
        }

        // Nothing durable is changed before this governed call. Deferral can
        // therefore leave every row byte-for-byte as it was.
        var response = await prdb.AskAsync(
            apiKey,
            PrdbWork.Identification,
            (client, token) => client.Videos.Identify.PostAsync(
                new IdentifyVideosRequest
                {
                    IncludeVideoDetails = true,
                    Files =
                    [
                        .. arrivals.Select(arrival => new IdentifyVideoFileDto
                        {
                            Ref = arrival.Id.ToString("D"),
                            Filename = arrival.ArrivedName,
                            Filesize = arrival.SizeBytes,
                            OsHash = arrival.OsHash is { } hash ? FileHashes.ForPrdbLookup(hash) : null,
                            PHash = null,
                        }),
                    ],
                },
                cancellationToken: token),
            cancellationToken);

        var answers = response?.Results ?? throw new InvalidOperationException(
            "prdb answered Arrival Identification without results.");
        var byReference = answers
            .Where(answer => answer.Ref is not null)
            .ToDictionary(answer => answer.Ref!, StringComparer.Ordinal);
        if (answers.Count != arrivals.Count
            || arrivals.Any(arrival => !byReference.ContainsKey(arrival.Id.ToString("D"))))
        {
            throw new InvalidOperationException(
                "prdb did not return exactly one Arrival Identification result per submitted file.");
        }

        foreach (var answer in answers)
        {
            if (answer.Video is not null)
            {
                await details.WriteAsync(answer.Video, cancellationToken);
            }
        }

        var referencedVideos = answers
            .SelectMany(answer => answer.Candidates ?? [])
            .Concat(answers.Select(answer => answer.VideoId))
            .Concat(answers.Select(answer => answer.Video?.Id))
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        foreach (var videoId in referencedVideos)
        {
            await catalogue.VideoAsync(videoId, title: null, releaseDate: null, cancellationToken);
        }

        var now = time.GetUtcNow();
        await context.IdentificationOutcomes
            .Where(outcome => outcome.At < now - OutcomeWindow)
            .ExecuteDeleteAsync(cancellationToken);

        var arrivalIds = arrivals.Select(arrival => arrival.Id).ToArray();
        await context.ArrivingFileCandidates
            .Where(candidate => arrivalIds.Contains(candidate.ArrivingFileId))
            .ExecuteDeleteAsync(cancellationToken);

        var admitted = await context.GateAdmissions
            .Where(row => row.Gate == AfterDownloadGate.Name)
            .Select(row => row.Confidence)
            .ToListAsync(cancellationToken);

        foreach (var arrival in arrivals)
        {
            await ApplyAsync(
                arrival,
                byReference[arrival.Id.ToString("D")],
                admitted.ToHashSet(),
                now,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("prdb identified {Count} arriving Video File(s).", arrivals.Count);
        return RunResult.Handled(arrivals.Count);
    }

    private async Task ApplyAsync(
        ArrivingFileRow arrival,
        IdentifyVideoResultDto answer,
        IReadOnlySet<IdentificationConfidence> admitted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        arrival.VideoId = answer.VideoId ?? answer.Video?.Id;
        arrival.Confidence = Confidence(answer.Confidence);
        arrival.MatchedBy = Rung(answer.MatchedBy);
        arrival.SiteId = answer.Site?.Id;

        foreach (var candidate in (answer.Candidates ?? []).OfType<Guid>().Distinct())
        {
            context.ArrivingFileCandidates.Add(new ArrivingFileCandidateRow
            {
                ArrivingFileId = arrival.Id,
                VideoId = candidate,
            });
        }

        if (answer.Site?.Id is { } siteId)
        {
            await catalogue.SiteAsync(siteId, answer.Site.Title, network: null, cancellationToken);
        }

        var mayFile = arrival.VideoId is not null
            && arrival.Confidence is { } confidence
            && admitted.Contains(confidence);
        arrival.State = mayFile
            ? ArrivingFileState.AwaitingFiling
            : ArrivingFileState.AwaitingIdentification;
        arrival.Reason = mayFile ? null : ArrivingFileReason.Unidentified;

        context.IdentificationOutcomes.Add(new IdentificationOutcomeRow
        {
            At = now,
            Gate = AfterDownloadGate.Name,
            Outcome = Outcome(answer, arrival.Confidence),
        });
    }

    private static string Outcome(IdentifyVideoResultDto answer, IdentificationConfidence? confidence)
    {
        if ((answer.VideoId ?? answer.Video?.Id) is not null && confidence is not null)
        {
            return confidence.Value.ToString();
        }

        if (answer.Confidence == (int)IdentificationConfidence.Ambiguous
            || answer.Candidates is { Count: > 0 })
        {
            return IdentificationConfidence.Ambiguous.ToString();
        }

        return answer.Site?.Id is not null ? "SiteOnly" : "Unknown";
    }

    private static IdentificationConfidence? Confidence(int? value) => value switch
    {
        null => null,
        0 => IdentificationConfidence.None,
        1 => IdentificationConfidence.Partial,
        2 => IdentificationConfidence.Probable,
        3 => IdentificationConfidence.Strong,
        4 => IdentificationConfidence.Exact,
        5 => IdentificationConfidence.Ambiguous,
        _ => throw new InvalidOperationException("prdb returned an unknown Identification confidence."),
    };

    private static IdentificationRung? Rung(int? value) => value switch
    {
        null => null,
        0 => IdentificationRung.OsHash,
        1 => IdentificationRung.PHash,
        2 => IdentificationRung.Filename,
        3 => IdentificationRung.ReleaseName,
        4 => IdentificationRung.Site,
        _ => throw new InvalidOperationException("prdb returned an unknown Identification rung."),
    };
}
