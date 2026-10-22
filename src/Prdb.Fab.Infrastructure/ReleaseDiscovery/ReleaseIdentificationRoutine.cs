using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Serialization;

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
    VideoChangeCoverage coverage,
    PrdbGovernor governor,
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
        var now = time.GetUtcNow();
        var identifyAgainBefore = now - RecentWindow.RevalidateAfter;
        var candidates = await WorkSet(now).AsTracking()
            .OrderByDescending(row => row.IdentificationDetailsPending)
            .ThenBy(row => row.IdentificationState == IdentificationState.Awaiting ? 0 : 1)
            .ThenByDescending(row => row.SearchWasReason)
            .ThenBy(row => row.FirstSeenAt)
            .ThenBy(row => row.Id)
            .Take(BatchSize * 10)
            .ToListAsync(cancellationToken);

        // Bound row fan-out as well as distinct remote evidence. Remaining
        // exact duplicates reuse the durable answer on their next turn.
        var names = candidates.Where(row => !row.IdentificationDetailsPending)
            .Select(row => row.Title).Distinct(StringComparer.Ordinal).Take(BatchSize).ToHashSet(StringComparer.Ordinal);
        var releases = candidates.Where(row => row.IdentificationDetailsPending || names.Contains(row.Title)).ToList();
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

        var scope = await context.Installation.Select(row => row.PrdbUserHash).SingleAsync(cancellationToken)
            ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        var unasked = releases.Where(row => !row.IdentificationDetailsPending).ToList();
        var groups = unasked.GroupBy(row => InputKey(row.Title, scope), StringComparer.Ordinal).ToArray();
        var remote = new List<IGrouping<string, ReleaseRow>>();
        foreach (var group in groups)
        {
            var cached = await context.Releases
                .Where(row => row.IdentificationInputKey == group.Key && !row.IdentificationDetailsPending
                    && row.IdentificationAnswerJson != null && row.LastIdentifiedAt > identifyAgainBefore)
                .OrderByDescending(row => row.LastIdentifiedAt)
                .Select(row => new { row.IdentificationAnswerJson, row.LastIdentifiedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (cached is null) { remote.Add(group); continue; }
            var answer = await ReadAnswerAsync(cached.IdentificationAnswerJson!, cancellationToken);
            foreach (var release in group)
                await CheckpointAsync(release, group.Key, answer, cached.LastIdentifiedAt!.Value, cancellationToken);
        }
        // A quota refusal of the uncached names must not discard cache hits.
        await context.SaveChangesAsync(cancellationToken);
        if (remote.Count > 0)
        {
            var response = await prdb.AskAsync(apiKey, PrdbWork.ReleaseIdentification,
                (client, token) => client.Videos.Identify.PostAsync(new IdentifyVideosRequest
                {
                    IncludeVideoDetails = true,
                    Files = [.. remote.Select(group => new IdentifyVideoFileDto
                    {
                        Ref = group.First().Id.ToString(CultureInfo.InvariantCulture),
                        Filename = group.First().Title,
                        // Size and hashes are absent on this path. If evidence
                        // is added, it must also populate IdentificationInput.
                    })],
                }, cancellationToken: token), cancellationToken);
            var returned = response?.Results ?? throw new InvalidOperationException("prdb answered Identification without results.");
            var answersByRef = returned.Where(answer => answer.Ref is not null)
                .ToDictionary(answer => answer.Ref!, StringComparer.Ordinal);
            if (returned.Count != remote.Count || remote.Any(group =>
                !answersByRef.ContainsKey(group.First().Id.ToString(CultureInfo.InvariantCulture))))
                throw new InvalidOperationException("prdb did not return exactly one Identification result per submitted input.");
            foreach (var answer in returned) Validate(answer);
            foreach (var group in remote)
            {
                var answer = answersByRef[group.First().Id.ToString(CultureInfo.InvariantCulture)];
                foreach (var release in group)
                    await CheckpointAsync(release, group.Key, answer, now, cancellationToken);
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        governor.Costs.Identification(remote.Count, unasked.Count - remote.Count, 0);
        var byReference = new Dictionary<string, IdentifyVideoResultDto>(StringComparer.Ordinal);
        foreach (var release in releases)
        {
            byReference[release.Id.ToString(CultureInfo.InvariantCulture)] =
                await ReadAnswerAsync(release.IdentificationAnswerJson!, cancellationToken);
        }
        var answers = byReference.Values.ToArray();
        var referencedVideos = answers
            .SelectMany(answer => answer.Candidates ?? [])
            .Concat(answers.Select(answer => answer.VideoId))
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        foreach (var inline in answers.Select(answer => answer.Video).OfType<VideoDetailDto>()
            .GroupBy(detail => detail.Id).Select(group => group.First()))
        {
            await details.WriteAsync(inline, cancellationToken);
        }
        var current = await CurrentDetailsAsync(referencedVideos, cancellationToken);
        var missing = referencedVideos.Except(current).ToArray();
        governor.Costs.Identification(0, 0, current.Count);
        await FetchDetailsAsync(apiKey, missing, now, cancellationToken);
        current = await CurrentDetailsAsync(referencedVideos, cancellationToken);

        // A candidate can be omitted by a concurrent upstream deletion. Keep
        // the outside id as a stub so the Candidate edge and its repair pin are
        // still durable; the repair pass is the authority that fills it later.
        foreach (var videoId in referencedVideos)
        {
            await catalogue.VideoAsync(videoId, title: null, releaseDate: null, cancellationToken);
        }

        await using var applying = await context.Database.BeginTransactionAsync(cancellationToken);
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
            if (answer.VideoId is { } matched && !current.Contains(matched))
            {
                release.DetailRetryAt = now.AddHours(1);
                continue;
            }
            await ApplyAsync(release, answer, release.LastIdentifiedAt ?? now, cancellationToken);
            release.IdentificationDetailsPending = false;
            release.DetailRetryAt = null;
            // Full inline documents have been projected. Keep only the small
            // authoritative answer for exact-input reuse until its expiry.
            answer.Video = null;
            release.IdentificationAnswerJson = await KiotaJsonSerializer.SerializeAsStringAsync(answer, false, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await applying.CommitAsync(cancellationToken);
        logger.LogInformation("prdb identified {Count} screened Release name(s).", releases.Count);

        var remains = await WorkSet(time.GetUtcNow()).AnyAsync(cancellationToken);
        return RunResult.Handled(releases.Count, remains ? TimeSpan.Zero : Cadence);
    }

    private IQueryable<ReleaseRow> WorkSet(DateTimeOffset now) => ReleaseIdentificationWork.Current(context, now);

    private static string InputKey(string title, string accountScope) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Scope = accountScope, Evidence = new IdentificationInput(title) })));

    private static async Task<IdentifyVideoResultDto> ReadAnswerAsync(string json, CancellationToken token) =>
        await KiotaJsonSerializer.DeserializeAsync<IdentifyVideoResultDto>(json, token)
        ?? throw new InvalidOperationException("A saved Identification answer is missing.");

    private static void Validate(IdentifyVideoResultDto answer)
    {
        if (answer.VideoId is null) return;
        _ = Confidence(answer.Confidence);
        _ = Rung(answer.MatchedBy);
        if (answer.Video?.Id is { } inlineId && inlineId != answer.VideoId)
            throw new InvalidOperationException("prdb returned inline details for a different Video.");
    }

    private static async Task CheckpointAsync(ReleaseRow release, string key, IdentifyVideoResultDto answer,
        DateTimeOffset at, CancellationToken token)
    {
        Validate(answer);
        answer.Ref = release.Id.ToString(CultureInfo.InvariantCulture);
        release.IdentificationInputKey = key;
        release.IdentificationAnswerJson = await KiotaJsonSerializer.SerializeAsStringAsync(answer, false, token);
        release.IdentificationDetailsPending = true;
        release.LastIdentifiedAt = at;
        release.IdentificationState = IdentificationState.Awaiting;
        release.AutomationPending = false;
        release.VideoId = null;
    }

    private async Task<List<Guid>> CurrentDetailsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var fresh = await coverage.FreshAsync(cancellationToken);
        var baseline = fresh ? await context.VideoChangeState.Select(row => (DateTimeOffset?)row.LowerBoundAt)
            .SingleOrDefaultAsync(cancellationToken) : null;
        var since = time.GetUtcNow() - RecentWindow.RevalidateAfter;
        return await context.CatalogueVideos.Where(row => ids.Contains(row.PrdbId) && row.Available
                && row.LastReadAt != default && (row.LastReadAt > since || fresh && row.ChangesBaselineAt == baseline))
            .Select(row => row.PrdbId).ToListAsync(cancellationToken);
    }

    private async Task FetchDetailsAsync(
        string apiKey,
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset checkpointAt,
        CancellationToken cancellationToken)
    {
        var completed = await context.CatalogueVideos
            .Where(row => ids.Contains(row.PrdbId) && row.LastReadAt >= checkpointAt)
            .Select(row => row.PrdbId).ToListAsync(cancellationToken);
        foreach (var batch in ids.Except(completed).Chunk(CatalogueRead.ABatch))
        {
            var read = await prdb.AskAsync(
                apiKey,
                PrdbWork.ReleaseIdentification,
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
        release.LastIdentifiedAt = now;
        release.AutomationPending = false;
        release.AutomationDecisionReason = null;
        release.AutomationDecisionAt = null;

        string outcome;

        if (answer.VideoId is { } videoId)
        {
            release.IdentificationState = IdentificationState.Matched;
            release.VideoId = await RequiredVideoAsync(videoId, cancellationToken);
            release.Confidence = Confidence(answer.Confidence);
            release.MatchedBy = Rung(answer.MatchedBy);
            release.AutomationPending = await context.WantedVideos.AnyAsync(
                wanted => wanted.VideoId == release.VideoId,
                cancellationToken);
            release.AutomationDecisionReason = null;
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
