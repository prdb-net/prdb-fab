using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Asks prdb which video each screened Release belongs to.</summary>
public sealed class ReleaseIdentificationRoutine(
    FabDbContext context,
    PrdbGateway prdb,
    VideoDetails details,
    CatalogueRows catalogue,
    TimeProvider time,
    ILogger<ReleaseIdentificationRoutine> logger) : IRoutine
{
    public const int BatchSize = 200;
    private static readonly TimeSpan OutcomeWindow = TimeSpan.FromDays(7);

    public string Name => DiscoveryRoutineNames.Identification;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromSeconds(10);

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var releases = await context.Releases
            .AsTracking()
            .Where(row => row.IdentificationState == IdentificationState.Awaiting)
            .OrderByDescending(row => row.SearchWasReason)
            .ThenBy(row => row.FirstSeenAt)
            .ThenBy(row => row.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (releases.Count == 0)
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

        var response = await prdb.AskAsync(
            apiKey,
            PrdbWork.Identification,
            (client, token) => client.Videos.Identify.PostAsync(
                new IdentifyVideosRequest
                {
                    IncludeVideoDetails = false,
                    Files =
                    [
                        .. releases.Select(release => new IdentifyVideoFileDto
                        {
                            Ref = release.Id.ToString(CultureInfo.InvariantCulture),
                            Filename = release.Title,
                            Filesize = null,
                            OsHash = null,
                            PHash = null,
                        }),
                    ],
                },
                cancellationToken: token),
            cancellationToken);

        var answers = response?.Results ?? throw new InvalidOperationException(
            "prdb answered Release Identification without results.");
        var byReference = answers
            .Where(answer => answer.Ref is not null)
            .ToDictionary(answer => answer.Ref!, StringComparer.Ordinal);

        if (answers.Count != releases.Count
            || releases.Any(release => !byReference.ContainsKey(release.Id.ToString(CultureInfo.InvariantCulture))))
        {
            throw new InvalidOperationException(
                "prdb did not return exactly one Release Identification result per submitted name.");
        }

        var referencedVideos = answers
            .SelectMany(answer => answer.Candidates ?? [])
            .Concat(answers.Select(answer => answer.VideoId))
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        await FetchDetailsAsync(apiKey, referencedVideos, cancellationToken);

        // A candidate can be omitted by a concurrent upstream deletion. Keep
        // the outside id as a stub so the Candidate edge and its repair pin are
        // still durable; the repair pass is the authority that fills it later.
        foreach (var videoId in referencedVideos)
        {
            await catalogue.VideoAsync(videoId, title: null, releaseDate: null, cancellationToken);
        }

        var now = time.GetUtcNow();
        await context.IdentificationOutcomes
            .Where(outcome => outcome.At < now - OutcomeWindow)
            .ExecuteDeleteAsync(cancellationToken);

        var releaseIds = releases.Select(release => release.Id).ToArray();
        await context.ReleaseCandidates
            .Where(candidate => releaseIds.Contains(candidate.ReleaseId))
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var release in releases)
        {
            var answer = byReference[release.Id.ToString(CultureInfo.InvariantCulture)];
            await ApplyAsync(release, answer, now, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("prdb identified {Count} screened Release name(s).", releases.Count);

        return RunResult.Handled(releases.Count);
    }

    private async Task FetchDetailsAsync(
        string apiKey,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        foreach (var batch in ids.Chunk(Backfill.ABatch))
        {
            var read = await prdb.AskAsync(
                apiKey,
                PrdbWork.Identification,
                (client, token) => client.Videos.Batch.PostAsync(
                    new GetVideosByIdsRequest { Ids = [.. batch.Select(id => (Guid?)id)] },
                    cancellationToken: token),
                cancellationToken);

            foreach (var detail in read ?? [])
            {
                await details.WriteAsync(detail, cancellationToken);
            }
        }
    }

    private async Task ApplyAsync(
        ReleaseRow release,
        IdentifyVideoResultDto answer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        release.VideoId = null;
        release.Confidence = null;
        release.MatchedBy = null;
        release.SiteId = null;
        release.SearchWasReason = false;

        string outcome;

        if (answer.VideoId is { } videoId)
        {
            release.IdentificationState = IdentificationState.Matched;
            release.VideoId = await RequiredVideoAsync(videoId, cancellationToken);
            release.Confidence = Confidence(answer.Confidence);
            release.MatchedBy = Rung(answer.MatchedBy);
            outcome = release.Confidence.Value.ToString();
        }
        else if (answer.Confidence == (int)IdentificationConfidence.Ambiguous
                 || answer.Candidates is { Count: > 0 })
        {
            release.IdentificationState = IdentificationState.Ambiguous;

            foreach (var candidate in (answer.Candidates ?? []).OfType<Guid>().Distinct())
            {
                context.ReleaseCandidates.Add(new ReleaseCandidateRow
                {
                    ReleaseId = release.Id,
                    VideoId = await RequiredVideoAsync(candidate, cancellationToken),
                });
            }

            outcome = IdentificationState.Ambiguous.ToString();
        }
        else if (answer.Site?.Id is { } siteId)
        {
            release.IdentificationState = IdentificationState.SiteOnly;
            release.SiteId = await catalogue.SiteAsync(
                siteId,
                answer.Site.Title,
                network: null,
                cancellationToken);
            outcome = IdentificationState.SiteOnly.ToString();
        }
        else
        {
            release.IdentificationState = IdentificationState.Unknown;
            outcome = IdentificationState.Unknown.ToString();
        }

        context.IdentificationOutcomes.Add(new IdentificationOutcomeRow
        {
            At = now,
            Gate = "BeforeDownload",
            Outcome = outcome,
        });
    }

    private async Task<long> RequiredVideoAsync(Guid prdbId, CancellationToken cancellationToken) =>
        await catalogue.FindVideoAsync(prdbId, cancellationToken)
        ?? throw new InvalidOperationException("A referenced prdb video was not written to the Catalogue.");

    private static IdentificationConfidence Confidence(int? value) => value switch
    {
        0 => IdentificationConfidence.None,
        1 => IdentificationConfidence.Partial,
        2 => IdentificationConfidence.Probable,
        3 => IdentificationConfidence.Strong,
        4 => IdentificationConfidence.Exact,
        5 => IdentificationConfidence.Ambiguous,
        _ => throw new InvalidOperationException("prdb returned an unknown Identification confidence."),
    };

    private static IdentificationRung Rung(int? value) => value switch
    {
        0 => IdentificationRung.OsHash,
        1 => IdentificationRung.PHash,
        2 => IdentificationRung.Filename,
        3 => IdentificationRung.ReleaseName,
        4 => IdentificationRung.Site,
        _ => throw new InvalidOperationException("prdb returned an unknown Identification rung."),
    };
}
