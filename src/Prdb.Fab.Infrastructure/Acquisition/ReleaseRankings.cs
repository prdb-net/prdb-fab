using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Acquisition;
using Prdb.Fab.Core.ReleaseDiscovery;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>Builds ADR 0008's ranking from the local cache and Download record.</summary>
public sealed class ReleaseRankings(FabDbContext context, TimeProvider time)
{
    public async Task<VideoReleaseRanking?> ForVideoAsync(
        Guid videoId,
        bool observeDecision,
        CancellationToken cancellationToken = default)
    {
        var video = await context.CatalogueVideos
            .AsNoTracking()
            .Where(row => row.PrdbId == videoId)
            .Select(row => new { row.Id, row.PrdbId, row.Title })
            .SingleOrDefaultAsync(cancellationToken);

        if (video is null) return null;

        var releases = await context.Releases
            .AsNoTracking()
            .Where(row => row.VideoId == video.Id && row.IdentificationState == IdentificationState.Matched)
            .Include(row => row.Indexer)
            .ToListAsync(cancellationToken);

        var consumed = await context.Downloads
            .AsNoTracking()
            .Where(row => row.VideoId == videoId)
            .Select(row => new { row.IndexerId, row.DerivedReleaseId })
            .ToListAsync(cancellationToken);
        var consumedKeys = consumed
            .Select(row => Key(row.IndexerId, row.DerivedReleaseId))
            .ToHashSet(StringComparer.Ordinal);

        var ranked = ReleaseRanking.Order(releases.Select(row => new RankableRelease(
            row.Id,
            row.IndexerId,
            row.DerivedReleaseId,
            row.Size,
            row.Confidence,
            row.Indexer?.Rank ?? int.MaxValue,
            row.Password is not null && row.Password != "0",
            consumedKeys.Contains(Key(row.IndexerId, row.DerivedReleaseId)),
            !string.IsNullOrWhiteSpace(row.DownloadUrl))));

        var byId = releases.ToDictionary(row => row.Id);
        var choices = ranked.Ranked.Select(item => Choice(byId[item.Release.Id], item.Position, null)).ToArray();
        var exclusions = ranked.Excluded.Select(item => Choice(byId[item.Release.Id], null, item.Reason)).ToArray();

        if (observeDecision)
        {
            var now = time.GetUtcNow();
            await context.ReleasesNotDownloaded
                .Where(row => row.At < now.AddDays(-7))
                .ExecuteDeleteAsync(cancellationToken);

            context.ReleasesNotDownloaded.AddRange(exclusions.Select(item => new ReleaseNotDownloadedRow
            {
                At = now,
                Reason = item.Exclusion!.Value.ToString(),
            }));
            await context.SaveChangesAsync(cancellationToken);
        }

        var budget = await context.Installation.Select(row => row.RetryBudget).SingleAsync(cancellationToken);
        return new(
            video.PrdbId,
            video.Title,
            budget,
            consumed.Count,
            choices,
            exclusions);
    }

    private static ReleaseChoice Choice(ReleaseRow row, int? position, ReleaseExclusion? exclusion) => new(
        row.Id,
        row.IndexerId,
        row.Indexer?.Name ?? string.Empty,
        row.DerivedReleaseId,
        row.Title,
        row.Size,
        row.Confidence,
        position,
        exclusion);

    private static string Key(Guid indexerId, string releaseId) => $"{indexerId:N}\0{releaseId}";
}

public sealed record VideoReleaseRanking(
    Guid VideoId,
    string VideoTitle,
    int RetryBudget,
    int DownloadsSpent,
    IReadOnlyList<ReleaseChoice> Ranked,
    IReadOnlyList<ReleaseChoice> Excluded)
{
    public ReleaseChoice? Find(long releaseId) =>
        Ranked.Concat(Excluded).SingleOrDefault(release => release.Id == releaseId);
}

public sealed record ReleaseChoice(
    long Id,
    Guid IndexerId,
    string IndexerName,
    string DerivedReleaseId,
    string Title,
    long? Size,
    IdentificationConfidence? Confidence,
    int? Position,
    ReleaseExclusion? Exclusion);
