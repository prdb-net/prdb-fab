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
        var ranked = Rank(
            releases,
            consumed.Select(row => Key(row.IndexerId, row.DerivedReleaseId)).ToHashSet(StringComparer.Ordinal));

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

    /// <summary>
    /// Which Videos can start another Download right now, in one bounded read
    /// for a catalogue page. This is the card-sized view of the same ranking
    /// and retry budget used by <see cref="ForVideoAsync"/> and by submission.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> ReadyVideosAsync(
        IReadOnlyCollection<Guid> videoIds,
        CancellationToken cancellationToken = default)
    {
        if (videoIds.Count == 0) return new HashSet<Guid>();

        var videos = await context.CatalogueVideos
            .AsNoTracking()
            .Where(row => videoIds.Contains(row.PrdbId))
            .Select(row => new { row.Id, row.PrdbId })
            .ToListAsync(cancellationToken);
        var localIds = videos.Select(row => row.Id).ToArray();
        var releases = await context.Releases
            .AsNoTracking()
            .Where(row => row.VideoId.HasValue
                && localIds.Contains(row.VideoId.Value)
                && row.IdentificationState == IdentificationState.Matched)
            .Include(row => row.Indexer)
            .ToListAsync(cancellationToken);
        var downloads = await context.Downloads
            .AsNoTracking()
            .Where(row => videoIds.Contains(row.VideoId))
            .Select(row => new { row.VideoId, row.IndexerId, row.DerivedReleaseId })
            .ToListAsync(cancellationToken);
        var budget = await context.Installation
            .Select(row => row.RetryBudget)
            .SingleAsync(cancellationToken);

        var releasesByVideo = releases.ToLookup(row => row.VideoId!.Value);
        var downloadsByVideo = downloads.ToLookup(row => row.VideoId);
        var ready = new HashSet<Guid>();
        foreach (var video in videos)
        {
            var consumed = downloadsByVideo[video.PrdbId]
                .Select(row => Key(row.IndexerId, row.DerivedReleaseId))
                .ToHashSet(StringComparer.Ordinal);
            if (downloadsByVideo[video.PrdbId].Count() < budget
                && Rank(releasesByVideo[video.Id], consumed).Ranked.Count > 0)
            {
                ready.Add(video.PrdbId);
            }
        }

        return ready;
    }

    private static ReleaseRankingResult Rank(
        IEnumerable<ReleaseRow> releases,
        IReadOnlySet<string> consumedKeys) =>
        ReleaseRanking.Order(releases.Select(row => new RankableRelease(
            row.Id,
            row.IndexerId,
            row.DerivedReleaseId,
            row.Size,
            row.Confidence,
            row.Indexer?.Rank ?? int.MaxValue,
            row.Password is not null && row.Password != "0",
            consumedKeys.Contains(Key(row.IndexerId, row.DerivedReleaseId)),
            !string.IsNullOrWhiteSpace(row.DownloadUrl))));

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
