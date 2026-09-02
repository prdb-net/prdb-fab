using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.Automation;
using Prdb.Fab.Infrastructure.Acquisition;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>
/// The one Release table, narrowed by the browse context that led to it.
/// Every query is over the Catalogue and Indexer Cache; reading this service
/// performs no remote work.
/// </summary>
public sealed class ReleaseBrowse(
    FabDbContext context,
    ReleaseRankings rankings,
    DownloadBrowse downloads,
    RecentWindowCoverage recentWindow)
{
    public const int APage = 50;

    public async Task<ReleasePage?> VideoAsync(
        Guid prdbId,
        IdentificationState? state,
        Guid? indexerId,
        int page,
        CancellationToken cancellationToken)
    {
        var selected = await context.CatalogueVideos
            .AsNoTracking()
            .Where(row => row.PrdbId == prdbId)
            .Select(row => new { row.Id, row.PrdbId, row.Title })
            .SingleOrDefaultAsync(cancellationToken);

        if (selected is null) return null;

        var ranking = await rankings.ForVideoAsync(prdbId, observeDecision: false, cancellationToken);
        var history = await downloads.ForVideoAsync(prdbId, cancellationToken);
        var heldQualities = await context.VideoFiles
            .AsNoTracking()
            .Where(row => row.LibraryEntryVideoId == prdbId)
            .Select(row => row.QualityLabel)
            .Distinct()
            .OrderBy(quality => quality)
            .ToListAsync(cancellationToken);
        return await ReadAsync(
                new ReleaseContext(ReleaseContextKind.Video, selected.PrdbId, selected.Title),
                context.Releases.Where(row =>
                    row.VideoId == selected.Id
                    || context.ReleaseCandidates.Any(candidate =>
                        candidate.ReleaseId == row.Id && candidate.VideoId == selected.Id)),
                state,
                indexerId,
                page,
                ranking,
                ranking is null
                    ? null
                    : new VideoAcquisition(
                        ranking.DownloadsSpent,
                        ranking.RetryBudget,
                        ranking.Ranked.FirstOrDefault(),
                        history,
                        heldQualities),
                cancellationToken);
    }

    public async Task<ReleasePage?> SiteAsync(
        Guid prdbId,
        IdentificationState? state,
        Guid? indexerId,
        int page,
        CancellationToken cancellationToken)
    {
        var selected = await context.CatalogueSites
            .AsNoTracking()
            .Where(row => row.PrdbId == prdbId)
            .Select(row => new { row.Id, row.PrdbId, row.Title })
            .SingleOrDefaultAsync(cancellationToken);

        return selected is null
            ? null
            : await ReadAsync(
                new ReleaseContext(ReleaseContextKind.Site, selected.PrdbId, selected.Title),
                context.Releases.Where(row =>
                    row.SiteId == selected.Id
                    || (row.Video != null && row.Video.SiteId == selected.Id)
                    || context.ReleaseCandidates.Any(candidate =>
                        candidate.ReleaseId == row.Id
                        && candidate.Video != null
                        && candidate.Video.SiteId == selected.Id)),
                state,
                indexerId,
                page,
                ranking: null,
                acquisition: null,
                cancellationToken);
    }

    public async Task<ReleasePage?> ActorAsync(
        Guid prdbId,
        IdentificationState? state,
        Guid? indexerId,
        int page,
        CancellationToken cancellationToken)
    {
        var selected = await context.CatalogueActors
            .AsNoTracking()
            .Where(row => row.PrdbId == prdbId)
            .Select(row => new { row.Id, row.PrdbId, row.Name })
            .SingleOrDefaultAsync(cancellationToken);

        return selected is null
            ? null
            : await ReadAsync(
                new ReleaseContext(ReleaseContextKind.Actor, selected.PrdbId, selected.Name),
                context.Releases.Where(row =>
                    context.CatalogueVideoActors.Any(credit =>
                        credit.ActorId == selected.Id && credit.VideoId == row.VideoId)
                    || context.ReleaseCandidates.Any(candidate =>
                        candidate.ReleaseId == row.Id
                        && context.CatalogueVideoActors.Any(credit =>
                            credit.ActorId == selected.Id && credit.VideoId == candidate.VideoId))),
                state,
                indexerId,
                page,
                ranking: null,
                acquisition: null,
                cancellationToken);
    }

    private async Task<ReleasePage> ReadAsync(
        ReleaseContext selected,
        IQueryable<ReleaseRow> relevant,
        IdentificationState? state,
        Guid? indexerId,
        int page,
        VideoReleaseRanking? ranking,
        VideoAcquisition? acquisition,
        CancellationToken cancellationToken)
    {
        var wanted = Paging.Wanted(page);
        var availableIndexers = await relevant
            .Where(row => row.Indexer != null)
            .Select(row => new { row.IndexerId, row.Indexer!.Name, row.Indexer.Rank })
            .Distinct()
            .OrderBy(row => row.Name)
            .ThenBy(row => row.IndexerId)
            .ToListAsync(cancellationToken);

        if (state is not null)
        {
            relevant = relevant.Where(row => row.IdentificationState == state);
        }

        if (indexerId is not null)
        {
            relevant = relevant.Where(row => row.IndexerId == indexerId);
        }

        var total = await relevant.CountAsync(cancellationToken);
        var query = relevant
            .AsNoTracking()
            .Include(row => row.Indexer)
            .Include(row => row.Video)
            .ThenInclude(video => video!.Site)
            .Include(row => row.Site);

        var rankById = ranking?.Ranked.ToDictionary(release => release.Id) ?? [];
        var exclusionById = ranking?.Excluded.ToDictionary(release => release.Id) ?? [];
        var releases = ranking is null
            ? await query
                .OrderByDescending(row => row.FirstSeenAt)
                .ThenByDescending(row => row.Id)
                .Skip(Paging.Skip(wanted, APage))
                .Take(APage)
                .ToListAsync(cancellationToken)
            : (await query.ToListAsync(cancellationToken))
                .OrderBy(row => rankById.ContainsKey(row.Id) ? 0 : exclusionById.ContainsKey(row.Id) ? 1 : 2)
                .ThenBy(row => rankById.GetValueOrDefault(row.Id)?.Position ?? int.MaxValue)
                .ThenBy(row => exclusionById.GetValueOrDefault(row.Id)?.Exclusion)
                .ThenByDescending(row => row.FirstSeenAt)
                .ThenByDescending(row => row.Id)
                .Skip(Paging.Skip(wanted, APage))
                .Take(APage)
                .ToList();

        var releaseIds = releases.Select(row => row.Id).ToArray();
        var candidateRows = await context.ReleaseCandidates
            .AsNoTracking()
            .Where(row => releaseIds.Contains(row.ReleaseId))
            .Include(row => row.Video)
            .ThenInclude(video => video!.Site)
            .OrderBy(row => row.Video!.Title)
            .ThenBy(row => row.VideoId)
            .ToListAsync(cancellationToken);
        var candidates = candidateRows
            .GroupBy(row => row.ReleaseId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReleaseCandidate>)
                [
                    .. group.Select(row => new ReleaseCandidate(
                        row.Video!.PrdbId,
                        row.Video.Title,
                        row.Video.Site?.Title)),
                ]);

        var pageIndexerIds = releases.Select(row => row.IndexerId).Distinct().ToArray();
        var automationRules = await context.AutomationRules
            .AsNoTracking()
            .Where(row => row.Enabled)
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var automationEdges = await context.AutomationRuleIndexers
            .AsNoTracking()
            .Where(row => pageIndexerIds.Contains(row.IndexerId)
                && row.Indexer != null
                && row.Indexer.Enabled)
            .ToListAsync(cancellationToken);

        var rows = releases.Select(row => new ReleaseViewRow(
            row.Id,
            row.Title,
            new ReleaseIndexer(row.IndexerId, row.Indexer!.Name, row.Indexer.Rank),
            row.Size,
            row.FirstSeenAt,
            row.IdentificationState,
            row.Confidence,
            row.MatchedBy,
            row.IdentificationState == IdentificationState.Matched && row.Video is not null
                ? new IdentifiedVideo(row.Video.PrdbId, row.Video.Title, row.Video.Site?.Title)
                : null,
            candidates.GetValueOrDefault(row.Id, []),
            row.IdentificationState == IdentificationState.SiteOnly && row.Site is not null
                ? new SiteOnlyMatch(row.Site.PrdbId, row.Site.Title)
                : null,
            rankById.GetValueOrDefault(row.Id)?.Position,
            exclusionById.GetValueOrDefault(row.Id)?.Exclusion,
            [.. automationRules.Where(rule =>
                    automationEdges.Any(edge => edge.AutomationRuleId == rule.Id && edge.IndexerId == row.IndexerId)
                    && AutomationRules.SizeFits(row.Size, rule.MinimumSize, rule.MaximumSize))
                .Select(rule => new ApplicableAutomationRule(rule.Id, rule.Name))],
            row.AutomationDecisionReason)).ToList();

        return new ReleasePage(
            selected,
            rows,
            [.. availableIndexers.Select(row => new ReleaseIndexer(row.IndexerId, row.Name, row.Rank))],
            wanted,
            APage,
            total,
            acquisition,
            await recentWindow.ReadAsync(cancellationToken));
    }
}

public enum ReleaseContextKind
{
    Video,
    Site,
    Actor,
}

public sealed record ReleaseContext(ReleaseContextKind Kind, Guid PrdbId, string Title);

public sealed record ReleaseIndexer(Guid Id, string Name, int Rank);

public sealed record IdentifiedVideo(Guid PrdbId, string Title, string? Site);

public sealed record ReleaseCandidate(Guid PrdbId, string Title, string? Site);

public sealed record SiteOnlyMatch(Guid PrdbId, string Title);

public sealed record ApplicableAutomationRule(Guid Id, string Name);

public sealed record ReleaseViewRow(
    long Id,
    string Title,
    ReleaseIndexer Indexer,
    long? Size,
    DateTimeOffset FirstSeenAt,
    IdentificationState IdentificationState,
    IdentificationConfidence? Confidence,
    IdentificationRung? MatchedBy,
    IdentifiedVideo? Video,
    IReadOnlyList<ReleaseCandidate> Candidates,
    SiteOnlyMatch? SiteOnlyMatch,
    int? RankingPosition,
    ReleaseExclusion? RankingExclusion,
    IReadOnlyList<ApplicableAutomationRule> ApplicableRules,
    AutomationDecisionReason? AutomaticDecisionReason);

public sealed record ReleasePage(
    ReleaseContext Context,
    IReadOnlyList<ReleaseViewRow> Releases,
    IReadOnlyList<ReleaseIndexer> Indexers,
    int Page,
    int PageSize,
    int Total,
    VideoAcquisition? Acquisition,
    RecentWindowCoverageState RecentWindow);

public sealed record VideoAcquisition(
    int DownloadsSpent,
    int RetryBudget,
    ReleaseChoice? NextRelease,
    IReadOnlyList<DownloadSelectionRow> Downloads,
    IReadOnlyList<string> HeldQualities);
