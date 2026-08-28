using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>
/// The one Release table, narrowed by the browse context that led to it.
/// Every query is over the Catalogue and Indexer Cache; reading this service
/// performs no remote work.
/// </summary>
public sealed class ReleaseBrowse(FabDbContext context)
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

        return selected is null
            ? null
            : await ReadAsync(
                new ReleaseContext(ReleaseContextKind.Video, selected.PrdbId, selected.Title),
                context.Releases.Where(row =>
                    row.VideoId == selected.Id
                    || context.ReleaseCandidates.Any(candidate =>
                        candidate.ReleaseId == row.Id && candidate.VideoId == selected.Id)),
                state,
                indexerId,
                page,
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
                cancellationToken);
    }

    private async Task<ReleasePage> ReadAsync(
        ReleaseContext selected,
        IQueryable<ReleaseRow> relevant,
        IdentificationState? state,
        Guid? indexerId,
        int page,
        CancellationToken cancellationToken)
    {
        var wanted = Math.Max(page, 1);
        var availableIndexers = await relevant
            .Where(row => row.Indexer != null)
            .Select(row => new { row.IndexerId, row.Indexer!.Name })
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
        var releases = await relevant
            .AsNoTracking()
            .Include(row => row.Indexer)
            .Include(row => row.Video)
            .ThenInclude(video => video!.Site)
            .Include(row => row.Site)
            .OrderByDescending(row => row.FirstSeenAt)
            .ThenByDescending(row => row.Id)
            .Skip((wanted - 1) * APage)
            .Take(APage)
            .ToListAsync(cancellationToken);

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

        var rows = releases.Select(row => new ReleaseViewRow(
            row.Id,
            row.Title,
            new ReleaseIndexer(row.IndexerId, row.Indexer!.Name),
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
                : null)).ToList();

        return new ReleasePage(
            selected,
            rows,
            [.. availableIndexers.Select(row => new ReleaseIndexer(row.IndexerId, row.Name))],
            wanted,
            APage,
            total);
    }
}

public enum ReleaseContextKind
{
    Video,
    Site,
    Actor,
}

public sealed record ReleaseContext(ReleaseContextKind Kind, Guid PrdbId, string Title);

public sealed record ReleaseIndexer(Guid Id, string Name);

public sealed record IdentifiedVideo(Guid PrdbId, string Title, string? Site);

public sealed record ReleaseCandidate(Guid PrdbId, string Title, string? Site);

public sealed record SiteOnlyMatch(Guid PrdbId, string Title);

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
    SiteOnlyMatch? SiteOnlyMatch);

public sealed record ReleasePage(
    ReleaseContext Context,
    IReadOnlyList<ReleaseViewRow> Releases,
    IReadOnlyList<ReleaseIndexer> Indexers,
    int Page,
    int PageSize,
    int Total);
