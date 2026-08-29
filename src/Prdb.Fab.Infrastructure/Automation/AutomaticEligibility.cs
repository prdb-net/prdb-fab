using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Automation;

/// <summary>Reads every current fact ADR 0007 requires before Ranking.</summary>
public sealed class AutomaticEligibility(FabDbContext context)
{
    public async Task<IReadOnlyDictionary<long, AutomaticReleaseEligibility>> ForVideoAsync(
        Guid videoId,
        IReadOnlyCollection<ReleaseChoice> releases,
        CancellationToken cancellationToken = default)
    {
        if (releases.Count == 0) return new Dictionary<long, AutomaticReleaseEligibility>();

        var localVideoId = await context.CatalogueVideos
            .Where(row => row.PrdbId == videoId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (localVideoId is null) return releases.ToDictionary(
            release => release.Id,
            _ => AutomaticReleaseEligibility.Refused(AutomationDecisionReason.NotWanted));

        var wanted = await context.WantedVideos.AnyAsync(row => row.VideoId == localVideoId, cancellationToken);
        var held = await context.LibraryEntries.AnyAsync(row => row.VideoId == videoId, cancellationToken);
        var review = await context.ArrivingFiles.AnyAsync(
            row => row.VideoId == videoId && row.Reason != null,
            cancellationToken);
        var admissions = await context.GateAdmissions
            .Where(row => row.Gate == BeforeDownloadGate.Name)
            .Select(row => row.Confidence)
            .ToListAsync(cancellationToken);
        var rules = await context.AutomationRules
            .Where(row => row.Enabled)
            .ToListAsync(cancellationToken);
        var edges = await context.AutomationRuleIndexers
            .Where(row => rules.Select(rule => rule.Id).Contains(row.AutomationRuleId)
                && row.Indexer != null
                && row.Indexer.Enabled)
            .ToListAsync(cancellationToken);
        var downloads = await context.Downloads
            .Where(row => row.VideoId == videoId)
            .ToListAsync(cancellationToken);
        var installation = await context.Installation
            .Select(row => new { row.RetryBudget, row.AutomaticDownloadCap })
            .SingleAsync(cancellationToken);
        var unfinishedAutomatic = await context.Downloads.CountAsync(
            row => !row.OriginIsPerson && row.State == DownloadState.Outstanding,
            cancellationToken);

        var admitted = admissions.ToHashSet();
        var result = new Dictionary<long, AutomaticReleaseEligibility>();
        foreach (var release in releases)
        {
            if (!wanted)
            {
                result[release.Id] = AutomaticReleaseEligibility.Refused(AutomationDecisionReason.NotWanted);
                continue;
            }
            if (held)
            {
                result[release.Id] = AutomaticReleaseEligibility.Refused(AutomationDecisionReason.HeldVideo);
                continue;
            }
            if (review)
            {
                result[release.Id] = AutomaticReleaseEligibility.Waiting(AutomationDecisionReason.OpenReviewQueue);
                continue;
            }
            if (!BeforeDownloadGate.Admits(videoId, release.Confidence, admitted))
            {
                result[release.Id] = AutomaticReleaseEligibility.Refused(AutomationDecisionReason.ConfidenceGate);
                continue;
            }

            var indexerRuleIds = edges
                .Where(edge => edge.IndexerId == release.IndexerId)
                .Select(edge => edge.AutomationRuleId)
                .ToHashSet();
            var indexerRules = rules.Where(rule => indexerRuleIds.Contains(rule.Id)).ToArray();
            if (indexerRules.Length == 0)
            {
                result[release.Id] = AutomaticReleaseEligibility.Refused(AutomationDecisionReason.IndexerNotAllowed);
                continue;
            }

            var permitting = indexerRules
                .Where(rule => AutomationRules.SizeFits(release.Size, rule.MinimumSize, rule.MaximumSize))
                .Select(rule => new PermittingAutomationRule(rule.Id, rule.Name))
                .ToArray();
            if (permitting.Length == 0)
            {
                result[release.Id] = AutomaticReleaseEligibility.Refused(AutomationDecisionReason.Size);
                continue;
            }
            if (downloads.Any(row => row.State is DownloadState.Outstanding or DownloadState.Completed))
            {
                result[release.Id] = AutomaticReleaseEligibility.Waiting(AutomationDecisionReason.DownloadInFlight);
                continue;
            }
            if (downloads.Count >= installation.RetryBudget)
            {
                result[release.Id] = AutomaticReleaseEligibility.Refused(AutomationDecisionReason.RetryBudgetSpent);
                continue;
            }
            if (unfinishedAutomatic >= installation.AutomaticDownloadCap)
            {
                result[release.Id] = AutomaticReleaseEligibility.Waiting(AutomationDecisionReason.AutomaticDownloadCap);
                continue;
            }

            result[release.Id] = AutomaticReleaseEligibility.Allowed(permitting);
        }

        return result;
    }
}

public sealed record PermittingAutomationRule(Guid Id, string Name);
public sealed record AutomaticReleaseEligibility(
    bool Eligible,
    bool Wait,
    AutomationDecisionReason? Reason,
    IReadOnlyList<PermittingAutomationRule> Rules)
{
    public static AutomaticReleaseEligibility Allowed(IReadOnlyList<PermittingAutomationRule> rules) =>
        new(true, false, null, rules);
    public static AutomaticReleaseEligibility Refused(AutomationDecisionReason reason) =>
        new(false, false, reason, []);
    public static AutomaticReleaseEligibility Waiting(AutomationDecisionReason reason) =>
        new(false, true, reason, []);
}
