using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions;

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
        foreach (var release in releases.Where(row => row.IdentificationDetailsPending))
        {
            if (release.IdentificationInputKey == InputKey(release.Title, scope) && (release.IdentificationValidatedAt ?? release.LastIdentifiedAt) > identifyAgainBefore) continue;
            release.IdentificationDetailsPending = false;
            release.IdentificationAnswerJson = null;
            release.DetailRetryAt = null;
        }
        var unasked = releases.Where(row => !row.IdentificationDetailsPending).ToList();
        var groups = unasked.GroupBy(row => InputKey(row.Title, scope), StringComparer.Ordinal).ToArray();
        var remote = new List<IGrouping<string, ReleaseRow>>();
        foreach (var group in groups)
        {
            var cached = await context.Releases
                .Where(row => row.IdentificationInputKey == group.Key && !row.IdentificationDetailsPending
                    && row.IdentificationState != IdentificationState.Awaiting
                    && row.IdentificationAnswerJson != null
                    && (row.IdentificationValidatedAt ?? row.LastIdentifiedAt) > identifyAgainBefore)
                .OrderByDescending(row => row.IdentificationValidatedAt ?? row.LastIdentifiedAt)
                .Select(row => new { row.IdentificationAnswerJson, row.LastIdentifiedAt, row.IdentificationValidatedAt })
                .FirstOrDefaultAsync(cancellationToken);
            var answer = cached is null ? null : await ReadAnswerAsync(cached.IdentificationAnswerJson!, cancellationToken);
            if (cached is null || answer is null || !string.IsNullOrWhiteSpace(answer.FreshnessToken)
                && (answer.FreshnessExpiresAtUtc is null || answer.FreshnessExpiresAtUtc <= now))
            { remote.Add(group); continue; }
            foreach (var release in group)
                await RetainAsync(release, group.Key, answer, cached.LastIdentifiedAt!.Value,
                    cached.IdentificationValidatedAt ?? cached.LastIdentifiedAt.Value, cancellationToken);
        }
        // A quota refusal of the uncached names must not discard cache hits.
        await using (var retaining = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            await EnsureScopeAsync(apiKey, scope, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await retaining.CommitAsync(cancellationToken);
        }
        if (remote.Count > 0) await AskAsync(apiKey, scope, remote, now, cancellationToken);
        governor.Costs.Identification(0, unasked.Count - remote.Count, 0);
        // Unchanged settled answers retain candidates, confidence and decisions.
        // Only a replacement or a new Release needs projection and acquisition work.
        releases = releases.Where(row => row.IdentificationDetailsPending).ToList();
        if (releases.Count == 0)
            return RunResult.Handled(unasked.Count, await WorkSet(time.GetUtcNow()).AnyAsync(cancellationToken) ? TimeSpan.Zero : Cadence);
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
        await EnsureScopeAsync(apiKey, scope, cancellationToken);
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

    private async Task AskAsync(string apiKey, string scope, IReadOnlyList<IGrouping<string, ReleaseRow>> groups,
        DateTimeOffset now, CancellationToken token)
    {
        var held = new Dictionary<string, (IdentifyVideoResultDto Answer, DateTimeOffset At)>();
        foreach (var group in groups)
        {
            var retained = await context.Releases.Where(row => row.IdentificationInputKey == group.Key
                    && row.IdentificationAnswerJson != null && row.LastIdentifiedAt != null)
                .OrderByDescending(row => row.IdentificationValidatedAt ?? row.LastIdentifiedAt)
                .Select(row => new { row.IdentificationAnswerJson, row.LastIdentifiedAt }).FirstOrDefaultAsync(token);
            if (retained is not null)
                held[group.Key] = (await ReadAnswerAsync(retained.IdentificationAnswerJson!, token), retained.LastIdentifiedAt!.Value);
        }
        var files = groups.Select(group => new IdentifyVideoFileDto
        {
            Ref = group.First().Id.ToString(CultureInfo.InvariantCulture), Filename = group.First().Title,
            // Release Identification supplies no size or hashes. InputKey covers
            // precisely this complete tuple and the account, never a normalized name.
            FreshnessToken = held.TryGetValue(group.Key, out var saved) ? saved.Answer.FreshnessToken : null,
        }).ToList();
        // Expired handles cannot save ladder work. Avoid a larger validation
        // envelope when no input has a potentially usable server proof.
        if (held.Values.Any(saved => !string.IsNullOrWhiteSpace(saved.Answer.FreshnessToken)
                && saved.Answer.FreshnessExpiresAtUtc > now))
        {
            IdentifyFreshnessResponse? response;
            try
            {
                response = await prdb.AskAsync(apiKey, PrdbWork.ReleaseIdentification,
                    (client, ct) => client.Videos.Identify.Validate.PostAsync(new ValidateIdentificationRequest
                    { Files = files, Reidentify = true, IncludeFreshness = true, IncludeVideoDetails = true }, cancellationToken: ct), token);
            }
            catch (ApiException unsupported) when (unsupported.ResponseStatusCode is 404 or 405 or 501)
            { response = null; }
            if (response is not null)
            {
                var results = response.Results ?? throw new InvalidOperationException("prdb omitted Identification validation results.");
                if (results.Count != groups.Count || results.Select(r => r.Ref).Distinct().Count() != groups.Count
                    || files.Any(f => !results.Any(r => r.Ref == f.Ref)))
                    throw new InvalidOperationException("prdb did not return one validation per input.");
                var reidentified = 0;
                var unchanged = 0;
                var fallback = new List<IGrouping<string, ReleaseRow>>();
                // Validate the whole envelope before checkpointing any replacement.
                foreach (var group in groups)
                {
                    var result = results.Single(r => r.Ref == group.First().Id.ToString(CultureInfo.InvariantCulture));
                    switch (result.Status)
                    {
                        case IdentifyFreshnessResultDto_status.Unchanged:
                            if (result.Result is not null || !held.TryGetValue(group.Key, out var retained)
                                || string.IsNullOrWhiteSpace(retained.Answer.FreshnessToken)
                                || retained.Answer.FreshnessExpiresAtUtc is not { } expires || expires <= now)
                                throw new InvalidOperationException("prdb asserted freshness without a usable retained proof.");
                            break;
                        case IdentifyFreshnessResultDto_status.Identified:
                            if (result.Result is null) throw new InvalidOperationException("prdb omitted a replacement Identification.");
                            Validate(result.Result);
                            break;
                        case IdentifyFreshnessResultDto_status.Reidentify:
                            if (result.Result is not null) throw new InvalidOperationException("prdb returned an inconsistent validation outcome.");
                            break;
                        default: throw new InvalidOperationException("prdb returned an unknown Identification validation status.");
                    }
                }
                await using var saving = await context.Database.BeginTransactionAsync(token);
                await EnsureScopeAsync(apiKey, scope, token);
                foreach (var group in groups)
                {
                    var result = results.Single(r => r.Ref == group.First().Id.ToString(CultureInfo.InvariantCulture));
                    if (result.Status == IdentifyFreshnessResultDto_status.Unchanged)
                    {
                        var retained = held[group.Key];
                        foreach (var release in group)
                            await RetainAsync(release, group.Key, retained.Answer, retained.At, now, token);
                        unchanged++;
                    }
                    else if (result.Status == IdentifyFreshnessResultDto_status.Identified)
                    {
                        foreach (var release in group) await CheckpointAsync(release, group.Key, result.Result!, now, token);
                        if (held.ContainsKey(group.Key)) reidentified++;
                    }
                    else fallback.Add(group);
                }
                await context.SaveChangesAsync(token);
                await saving.CommitAsync(token);
                governor.Costs.Freshness(groups.Count, unchanged, reidentified);
                governor.Costs.Identification(groups.Count - unchanged - fallback.Count, 0, 0);
                if (fallback.Count > 0) await IdentifyAsync(apiKey, scope, fallback, now, token);
                return;
            }
        }
        await IdentifyAsync(apiKey, scope, groups, now, token);
    }

    private async Task IdentifyAsync(string key, string scope, IReadOnlyList<IGrouping<string, ReleaseRow>> groups, DateTimeOffset now, CancellationToken token)
    {
        var response = await prdb.AskAsync(key, PrdbWork.ReleaseIdentification,
            (client, ct) => client.Videos.Identify.PostAsync(new IdentifyVideosRequest
            {
                IncludeVideoDetails = true, IncludeFreshness = true,
                Files = [.. groups.Select(g => new IdentifyVideoFileDto
                { Ref = g.First().Id.ToString(CultureInfo.InvariantCulture), Filename = g.First().Title })],
            }, cancellationToken: ct), token);
        var results = response?.Results ?? throw new InvalidOperationException("prdb answered Identification without results.");
        if (results.Count != groups.Count || results.Select(r => r.Ref).Distinct().Count() != groups.Count
            || groups.Any(g => !results.Any(r => r.Ref == g.First().Id.ToString(CultureInfo.InvariantCulture))))
            throw new InvalidOperationException("prdb did not return exactly one Identification result per input.");
        foreach (var answer in results) Validate(answer);
        var reidentified = groups.Count(g => g.Any(r => r.IdentificationAnswerJson is not null));
        await using var saving = await context.Database.BeginTransactionAsync(token);
        await EnsureScopeAsync(key, scope, token);
        foreach (var group in groups)
        {
            var answer = results.Single(r => r.Ref == group.First().Id.ToString(CultureInfo.InvariantCulture));
            foreach (var release in group) await CheckpointAsync(release, group.Key, answer, now, token);
        }
        await context.SaveChangesAsync(token);
        await saving.CommitAsync(token);
        governor.Costs.Identification(groups.Count, 0, 0);
        governor.Costs.Freshness(0, 0, reidentified);
    }

    private async Task EnsureScopeAsync(string key, string scope, CancellationToken token)
    {
        var installation = await context.Installation.Select(i => new { i.PrdbApiKey, i.PrdbUserHash }).SingleAsync(token);
        var currentScope = installation.PrdbUserHash ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installation.PrdbApiKey ?? "")));
        if (installation.PrdbApiKey != key || currentScope != scope)
            throw new InvalidOperationException("The prdb account changed during Identification; its response was discarded.");
    }

    private static async Task RetainAsync(ReleaseRow release, string key, IdentifyVideoResultDto answer,
        DateTimeOffset identifiedAt, DateTimeOffset validatedAt, CancellationToken token)
    {
        var previous = release.IdentificationAnswerJson is null ? null
            : await ReadAnswerAsync(release.IdentificationAnswerJson, token);
        var same = previous is not null && previous.VideoId == answer.VideoId
            && previous.Confidence == answer.Confidence && previous.MatchedBy == answer.MatchedBy
            && previous.Site?.Id == answer.Site?.Id && previous.Site?.Title == answer.Site?.Title
            && (previous.Candidates ?? []).SequenceEqual(answer.Candidates ?? []);
        if (!same || release.IdentificationInputKey != key || release.IdentificationState == IdentificationState.Awaiting
            || release.IdentificationDetailsPending)
            await CheckpointAsync(release, key, answer, identifiedAt, token);
        else
        {
            answer.Ref = release.Id.ToString(CultureInfo.InvariantCulture);
            release.IdentificationAnswerJson = await KiotaJsonSerializer.SerializeAsStringAsync(answer, false, token);
            release.LastIdentifiedAt = identifiedAt;
        }
        release.IdentificationValidatedAt = validatedAt;
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
        release.IdentificationValidatedAt = at;
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
