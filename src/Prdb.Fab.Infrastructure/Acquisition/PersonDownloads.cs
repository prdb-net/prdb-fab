using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Connections;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Automation;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>Plans and performs the one person-originated acquisition action.</summary>
public sealed class PersonDownloads(
    FabDbContext context,
    ReleaseRankings rankings,
    NewznabGateway newznab,
    SabnzbdGateway sabnzbd,
    AutomaticEligibility automaticEligibility,
    AccountPreferences accountPreferences,
    TimeProvider time)
{
    /// <summary>
    /// Submits the next ranked release after a terminal failure. The ordinary
    /// action path is deliberately reused so reservation-before-network,
    /// category validation, budget accounting, and uncertain answers have one
    /// implementation.
    /// </summary>
    public async Task<DownloadVerdict?> RetryNextAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var ranking = await rankings.ForVideoAsync(videoId, observeDecision: false, cancellationToken);
        if (ranking is null || ranking.DownloadsSpent >= ranking.RetryBudget) return null;
        if (ranking.Ranked.FirstOrDefault() is not { } next) return DownloadVerdict.Planning(
            Guid.CreateVersion7(time.GetUtcNow()),
            DownloadPlanOutcome.NoReleasesLeft,
            DetailOf(DownloadPlanOutcome.NoReleasesLeft));

        return await DownloadCoreAsync(
            Guid.CreateVersion7(time.GetUtcNow()),
            videoId,
            next.Id,
            requireFailedHistory: true,
            automatic: false,
            cancellationToken);
    }

    public async Task<DownloadPreview?> PreviewAsync(
        Guid videoId,
        long releaseId,
        CancellationToken cancellationToken = default)
    {
        var ranking = await rankings.ForVideoAsync(videoId, observeDecision: false, cancellationToken);
        if (ranking is null || ranking.Find(releaseId) is not { } release) return null;

        var outcome = OutcomeOf(ranking, release);
        return new(
            outcome,
            outcome == DownloadPlanOutcome.Ready ? Guid.CreateVersion7(time.GetUtcNow()) : null,
            release,
            ranking.DownloadsSpent,
            ranking.RetryBudget,
            DetailOf(outcome));
    }

    public async Task<DownloadVerdict?> DownloadAsync(
        Guid downloadId,
        Guid videoId,
        long releaseId,
        CancellationToken cancellationToken = default) =>
        await DownloadCoreAsync(
            downloadId,
            videoId,
            releaseId,
            requireFailedHistory: false,
            automatic: false,
            cancellationToken);

    public async Task<DownloadVerdict?> DownloadBestAsync(
        Guid videoId,
        CancellationToken cancellationToken = default)
    {
        var ranking = await rankings.ForVideoAsync(videoId, observeDecision: false, cancellationToken);
        if (ranking is null) return null;
        if (ranking.Ranked.FirstOrDefault() is not { } best)
        {
            await PersistWantedIntentAsync(videoId, cancellationToken);
            var outcome = ranking.DownloadsSpent >= ranking.RetryBudget
                ? DownloadPlanOutcome.RetryBudgetSpent
                : DownloadPlanOutcome.NoReleasesLeft;
            return DownloadVerdict.Planning(
                Guid.CreateVersion7(time.GetUtcNow()),
                outcome,
                DetailOf(outcome));
        }

        return await DownloadCoreAsync(
            Guid.CreateVersion7(time.GetUtcNow()),
            videoId,
            best.Id,
            requireFailedHistory: false,
            automatic: false,
            cancellationToken);
    }

    /// <summary>
    /// Uses the same reservation, NZB retrieval and SABnzbd submission as a
    /// person's action, while recording every rule that currently permits it.
    /// </summary>
    public async Task<DownloadVerdict?> AutomaticDownloadAsync(
        Guid downloadId,
        Guid videoId,
        long releaseId,
        CancellationToken cancellationToken = default) =>
        await DownloadCoreAsync(
            downloadId,
            videoId,
            releaseId,
            requireFailedHistory: false,
            automatic: true,
            cancellationToken);

    /// <summary>Resumes a manual reservation that crashed before SABnzbd was called.</summary>
    public async Task<DownloadVerdict?> SubmitPendingAsync(
        Guid downloadId,
        CancellationToken cancellationToken = default)
    {
        var download = await context.Downloads
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == downloadId, cancellationToken);
        if (download is null) return null;
        if (download.SubmissionState != DownloadSubmissionState.Pending) return VerdictOf(download);

        var installation = await context.Installation.AsNoTracking().SingleAsync(cancellationToken);
        if (installation.SabnzbdUrl is null
            || installation.SabnzbdApiKey is null
            || installation.SabnzbdCategory is null)
        {
            return DownloadVerdict.Connection(downloadId, "SABnzbd is not configured.");
        }

        var storedRelease = await context.Releases
            .AsNoTracking()
            .Where(row => row.IndexerId == download.IndexerId
                && row.DerivedReleaseId == download.DerivedReleaseId
                && row.Video != null
                && row.Video.PrdbId == download.VideoId)
            .Select(row => new { row.DownloadUrl, IndexerUrl = row.Indexer!.Url })
            .SingleOrDefaultAsync(cancellationToken);
        if (storedRelease is null)
        {
            return DownloadVerdict.Indexer(downloadId, "The reserved Release is no longer in the local cache.");
        }

        var categories = await sabnzbd.CategoryNamesAsync(
            installation.SabnzbdUrl,
            installation.SabnzbdApiKey,
            cancellationToken);
        if (categories.Outcome != SabnzbdConnectionOutcome.Saved
            || !categories.Categories.Contains(installation.SabnzbdCategory, StringComparer.Ordinal))
        {
            return DownloadVerdict.Connection(downloadId, "SABnzbd could not be checked.");
        }

        var nzb = await newznab.NzbAsync(
            storedRelease.DownloadUrl,
            storedRelease.IndexerUrl,
            cancellationToken);
        if (nzb.Refusal is not null)
        {
            return DownloadVerdict.Indexer(downloadId, "The NZB could not be fetched from the indexer.");
        }

        var claimed = await context.Downloads
            .Where(row => row.Id == downloadId && row.SubmissionState == DownloadSubmissionState.Pending)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.SubmissionState, DownloadSubmissionState.Submitting),
                cancellationToken);
        if (claimed == 0) return VerdictOf((await ExistingAsync(downloadId, cancellationToken))!);

        var submission = await sabnzbd.SubmitAsync(
            installation.SabnzbdUrl,
            installation.SabnzbdApiKey,
            installation.SabnzbdCategory,
            download.SubmittedName,
            nzb.Bytes,
            cancellationToken);
        if (submission.Outcome != SabnzbdConnectionOutcome.Saved)
        {
            await context.Downloads
                .Where(row => row.Id == downloadId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.SubmissionState, DownloadSubmissionState.Unknown),
                    cancellationToken);
            return VerdictOf((await ExistingAsync(downloadId, cancellationToken))!);
        }

        if (submission.NzoId is { Length: > 0 } nzoId)
        {
            await context.Downloads
                .Where(row => row.Id == downloadId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.NzoId, nzoId)
                    .SetProperty(row => row.SubmissionState, DownloadSubmissionState.Submitted),
                    cancellationToken);
        }
        else
        {
            await context.Downloads
                .Where(row => row.Id == downloadId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.State, DownloadState.Failed)
                    .SetProperty(row => row.Cause, DownloadCause.Rejected)
                    .SetProperty(row => row.SubmissionState, DownloadSubmissionState.Submitted),
                    cancellationToken);
        }

        return VerdictOf((await ExistingAsync(downloadId, cancellationToken))!);
    }

    private async Task<DownloadVerdict?> DownloadCoreAsync(
        Guid downloadId,
        Guid videoId,
        long releaseId,
        bool requireFailedHistory,
        bool automatic,
        CancellationToken cancellationToken)
    {
        if (await ExistingAsync(downloadId, cancellationToken) is { } existing)
        {
            return VerdictOf(existing);
        }

        var ranking = await rankings.ForVideoAsync(videoId, observeDecision: true, cancellationToken);
        if (ranking is null || ranking.Find(releaseId) is not { } release) return null;

        var planOutcome = OutcomeOf(ranking, release);
        if (planOutcome != DownloadPlanOutcome.Ready)
        {
            if (!automatic)
            {
                await PersistWantedIntentAsync(videoId, cancellationToken);
            }
            return DownloadVerdict.Planning(downloadId, planOutcome, DetailOf(planOutcome));
        }

        var installation = await context.Installation.AsNoTracking().SingleAsync(cancellationToken);
        if (installation.SabnzbdUrl is null
            || installation.SabnzbdApiKey is null
            || installation.SabnzbdCategory is null)
        {
            if (!automatic) await PersistWantedIntentAsync(videoId, cancellationToken);
            return DownloadVerdict.Connection(downloadId, "SABnzbd is not configured.");
        }

        var categories = await sabnzbd.CategoryNamesAsync(
            installation.SabnzbdUrl,
            installation.SabnzbdApiKey,
            cancellationToken);
        if (categories.Outcome != SabnzbdConnectionOutcome.Saved
            || !categories.Categories.Contains(installation.SabnzbdCategory, StringComparer.Ordinal))
        {
            if (!automatic) await PersistWantedIntentAsync(videoId, cancellationToken);
            return DownloadVerdict.Connection(
                downloadId,
                categories.Outcome == SabnzbdConnectionOutcome.Saved
                    ? "The configured SABnzbd category no longer exists."
                    : "SABnzbd could not be checked.");
        }

        var storedRelease = await context.Releases
            .AsNoTracking()
            .Where(row => row.Id == releaseId)
            .Select(row => new
            {
                row.DownloadUrl,
                IndexerUrl = row.Indexer!.Url,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (storedRelease is null) return null;

        var nzb = await newznab.NzbAsync(
            storedRelease.DownloadUrl,
            storedRelease.IndexerUrl,
            cancellationToken);
        if (nzb.Refusal is not null)
        {
            if (!automatic) await PersistWantedIntentAsync(videoId, cancellationToken);
            return DownloadVerdict.Indexer(downloadId, "The NZB could not be fetched from the indexer.");
        }

        var now = time.GetUtcNow();
        var download = new DownloadRow
        {
            Id = downloadId,
            VideoId = videoId,
            IndexerId = release.IndexerId,
            DerivedReleaseId = release.DerivedReleaseId,
            SubmittedName = release.Title,
            State = DownloadState.Outstanding,
            SubmissionState = automatic
                ? DownloadSubmissionState.Submitted
                : DownloadSubmissionState.Pending,
            OutstandingSince = now,
            OriginIsPerson = !automatic,
            CreatedAt = now,
        };

        // Re-check the two spending constraints under the database's single
        // writer immediately before reserving the submission. No transaction
        // spans the network write.
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            if (await ExistingAsync(downloadId, cancellationToken) is { } raced)
            {
                await transaction.RollbackAsync(cancellationToken);
                return VerdictOf(raced);
            }

            if (requireFailedHistory && await context.Downloads.AnyAsync(
                    row => row.VideoId == videoId && row.State != DownloadState.Failed,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return DownloadVerdict.Planning(
                    downloadId,
                    DownloadPlanOutcome.ReleaseNotEligible,
                    "Another Download for this Video is already active; no automatic retry was submitted.");
            }

            IReadOnlyList<PermittingAutomationRule> originRules = [];
            if (automatic)
            {
                var current = await automaticEligibility.ForVideoAsync(videoId, [release], cancellationToken);
                if (!current.TryGetValue(release.Id, out var permission) || !permission.Eligible)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return DownloadVerdict.Planning(
                        downloadId,
                        DownloadPlanOutcome.ReleaseNotEligible,
                        "The automatic permission changed before the Download was reserved; nothing was submitted.");
                }

                originRules = permission.Rules;
            }

            var spent = await context.Downloads.CountAsync(row => row.VideoId == videoId, cancellationToken);
            if (spent >= installation.RetryBudget)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DownloadVerdict.Planning(
                    downloadId,
                    DownloadPlanOutcome.RetryBudgetSpent,
                    DetailOf(DownloadPlanOutcome.RetryBudgetSpent));
            }

            var consumed = await context.Downloads.AnyAsync(
                row => row.VideoId == videoId
                    && row.IndexerId == release.IndexerId
                    && row.DerivedReleaseId == release.DerivedReleaseId,
                cancellationToken);
            if (consumed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return DownloadVerdict.Planning(
                    downloadId,
                    DownloadPlanOutcome.ReleaseNotEligible,
                    "That Release has already been consumed for this Video.");
            }

            context.Downloads.Add(download);
            if (!automatic)
            {
                await accountPreferences.StageWantedAsync(videoId, now, cancellationToken);
            }
            context.DownloadOriginRules.AddRange(originRules.Select(rule => new DownloadOriginRuleRow
            {
                Id = Guid.CreateVersion7(now),
                DownloadId = download.Id,
                AutomationRuleId = rule.Id,
                RuleName = rule.Name,
            }));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (!automatic)
        {
            var claimed = await context.Downloads
                .Where(row => row.Id == download.Id && row.SubmissionState == DownloadSubmissionState.Pending)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.SubmissionState, DownloadSubmissionState.Submitting),
                    cancellationToken);
            if (claimed == 0)
            {
                return VerdictOf((await ExistingAsync(download.Id, cancellationToken))!);
            }
        }

        var submission = await sabnzbd.SubmitAsync(
            installation.SabnzbdUrl,
            installation.SabnzbdApiKey,
            installation.SabnzbdCategory,
            download.SubmittedName,
            nzb.Bytes,
            cancellationToken);

        if (submission.Outcome != SabnzbdConnectionOutcome.Saved)
        {
            await context.Downloads
                .Where(row => row.Id == download.Id)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(row => row.SubmissionState, DownloadSubmissionState.Unknown),
                    cancellationToken);
            // The request may have reached SABnzbd even though its answer did
            // not return. The durable reservation prevents a blind duplicate;
            // its missing nzo_id makes the uncertainty explicit.
            return new(
                DownloadOutcome.SubmissionUnknown,
                download.Id,
                download.State,
                download.Cause,
                download.NzoId,
                "The submission has no SABnzbd answer and will not be repeated blindly.");
        }

        if (submission.NzoId is { Length: > 0 } nzoId)
        {
            await context.Downloads
                .Where(row => row.Id == download.Id && row.State == DownloadState.Outstanding)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.NzoId, nzoId)
                    .SetProperty(row => row.SubmissionState, DownloadSubmissionState.Submitted), cancellationToken);
        }
        else
        {
            await context.Downloads
                .Where(row => row.Id == download.Id && row.State == DownloadState.Outstanding)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.State, DownloadState.Failed)
                    .SetProperty(row => row.Cause, DownloadCause.Rejected)
                    .SetProperty(row => row.SubmissionState, DownloadSubmissionState.Submitted), cancellationToken);
        }

        return VerdictOf((await ExistingAsync(download.Id, cancellationToken))!);
    }

    private async Task PersistWantedIntentAsync(Guid videoId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await accountPreferences.StageWantedAsync(videoId, time.GetUtcNow(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<DownloadRow?> ExistingAsync(Guid id, CancellationToken cancellationToken) =>
        context.Downloads.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);

    private static DownloadPlanOutcome OutcomeOf(VideoReleaseRanking ranking, ReleaseChoice release)
    {
        if (ranking.DownloadsSpent >= ranking.RetryBudget) return DownloadPlanOutcome.RetryBudgetSpent;
        if (ranking.Ranked.Count == 0) return DownloadPlanOutcome.NoReleasesLeft;
        return release.Exclusion is null ? DownloadPlanOutcome.Ready : DownloadPlanOutcome.ReleaseNotEligible;
    }

    private static string DetailOf(DownloadPlanOutcome outcome) => outcome switch
    {
        DownloadPlanOutcome.Ready => "This exact Release will be submitted to the configured SABnzbd category.",
        DownloadPlanOutcome.ReleaseNotEligible => "That Release is not eligible for this Video.",
        DownloadPlanOutcome.NoReleasesLeft => "No unconsumed Release is available for this Video.",
        DownloadPlanOutcome.RetryBudgetSpent => "The Retry Budget for this Video is spent.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static DownloadVerdict VerdictOf(DownloadRow row) => row switch
    {
        { SubmissionState: DownloadSubmissionState.Pending } => new(
            DownloadOutcome.Pending,
            row.Id,
            row.State,
            row.Cause,
            null,
            "The manual Download is durably queued for SABnzbd submission."),
        { NzoId.Length: > 0 } => new(
            DownloadOutcome.Submitted,
            row.Id,
            row.State,
            row.Cause,
            row.NzoId,
            "The Download was submitted to SABnzbd."),
        { State: DownloadState.Failed, Cause: DownloadCause.Rejected } => new(
            DownloadOutcome.Rejected,
            row.Id,
            row.State,
            row.Cause,
            null,
            "SABnzbd returned no nzo_id, so the Download is recorded as rejected."),
        _ => new(
            DownloadOutcome.SubmissionUnknown,
            row.Id,
            row.State,
            row.Cause,
            row.NzoId,
            "The submission has no SABnzbd answer and will not be repeated blindly."),
    };
}

public enum DownloadPlanOutcome
{
    Ready,
    ReleaseNotEligible,
    NoReleasesLeft,
    RetryBudgetSpent,
}

public enum DownloadOutcome
{
    Pending,
    Submitted,
    Rejected,
    SubmissionUnknown,
    ConnectionProblem,
    IndexerProblem,
    ReleaseNotEligible,
    NoReleasesLeft,
    RetryBudgetSpent,
}

public sealed record DownloadPreview(
    DownloadPlanOutcome Outcome,
    Guid? DownloadId,
    ReleaseChoice Release,
    int DownloadsSpent,
    int RetryBudget,
    string Detail);

public sealed record DownloadVerdict(
    DownloadOutcome Outcome,
    Guid DownloadId,
    DownloadState? State,
    DownloadCause? Cause,
    string? NzoId,
    string Detail)
{
    public static DownloadVerdict Planning(Guid id, DownloadPlanOutcome outcome, string detail) => new(
        Enum.Parse<DownloadOutcome>(outcome.ToString()), id, null, null, null, detail);

    public static DownloadVerdict Connection(Guid id, string detail) => new(
        DownloadOutcome.ConnectionProblem, id, null, null, null, detail);

    public static DownloadVerdict Indexer(Guid id, string detail) => new(
        DownloadOutcome.IndexerProblem, id, null, null, null, detail);
}
