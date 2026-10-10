using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Sdk.Generated.Models;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Global current states, retaining only the Recent Window and local interests.</summary>
public sealed class VideoFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue,
    VideoDetails details, CatalogueAvailability availability, CataloguePins pins, EntryFiles entryFiles, TimeProvider time)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.VideoChanges;
    public override bool StartsAtTheBeginning => false;
    public override PrdbWork Work => PrdbWork.CatalogueChanges;

    public override async Task<FeedPage> ReadAsync(string apiKey, FeedPosition from, int pageSize, CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(apiKey, Work, (client, token) => client.Videos.Changes.GetAsync(r =>
        {
            r.QueryParameters.Since = from.Since;
            r.QueryParameters.SinceId = from.SinceId;
            r.QueryParameters.PageSize = pageSize;
        }, token), cancellationToken);
        if (page is null) return FeedPage.Nothing;
        var applied = 0;
        if (pageSize != 1 || from != FeedPosition.TheBeginning)
        {
            var ids = (page.Items ?? []).Select(i => i.Video?.Id).OfType<Guid>().ToArray();
            var relevant = await pins.Pinned(Context.CatalogueVideos).Where(v => ids.Contains(v.PrdbId)).Select(v => v.PrdbId).ToHashSetAsync(cancellationToken);
            var recent = RecentWindow.BeginsAt(time.GetUtcNow());
            relevant.UnionWith(await Context.CatalogueVideos.Where(v => ids.Contains(v.PrdbId) && v.CreatedAtUtc >= recent).Select(v => v.PrdbId).ToListAsync(cancellationToken));
            foreach (var item in page.Items ?? [])
            {
                if (item.Video is not { Id: { } id, UpdatedAtUtc: { } stamp } video) continue;
                var deleted = video.IsDeleted is true || string.Equals(item.EventType, "deleted", StringComparison.OrdinalIgnoreCase);
                // Known old cache rows can be retired too, without keeping unknown history.
                if (deleted)
                {
                    await availability.UnavailableAsync(id, stamp, video.DeletedAtUtc, video.MergedIntoId, cancellationToken);
                    applied++;
                    continue;
                }
                if (!relevant.Contains(id) && video.CreatedAtUtc < recent) continue;
                if (!relevant.Contains(id) && video.CreatedAtUtc is null) continue;
                var previousImage = await entryFiles.ChosenImageIdAsync(id, cancellationToken);
                await details.WriteAsync(Detail(video), cancellationToken);
                var currentImage = await entryFiles.ChosenImageIdAsync(id, cancellationToken);
                await entryFiles.RefreshAsync(id, previousImage != currentImage, cancellationToken);
                applied++;
            }
        }
        return new(applied, page.HasMore ?? false, page.NextCursor?.UpdatedAtUtc, page.NextCursor?.Id, page.ServerTimeUtc, page.Items?.Count ?? 0);
    }

    private static VideoDetailDto Detail(VideoChangeVideoDto video) => new()
    {
        Id = video.Id, Title = video.Title, CreatedAtUtc = video.CreatedAtUtc, UpdatedAtUtc = video.UpdatedAtUtc,
        ReleaseDate = video.ReleaseDate, DurationMs = video.DurationMs, DurationSpreadMs = video.DurationSpreadMs,
        DurationFileCount = video.DurationFileCount, Description = video.Description, StashdbSceneId = video.StashdbSceneId,
        Site = video.Site is not { } site ? null : new VideoDetailSiteDto
        {
            Id = site.Id, Title = site.Title, Url = site.Url, PlatformId = site.PlatformId, PlatformKey = site.PlatformKey,
            PlatformTitle = site.PlatformTitle, AccountHandle = site.AccountHandle,
            Network = site.Network is not { } network ? null : new VideoDetailNetworkDto { Id = network.Id, Title = network.Title, Url = network.Url },
        },
        PreNames = (video.PreNames ?? []).Select(p => new VideoDetailPreNameDto { Id = p.Id, Title = p.Title }).ToList(),
        Actors = (video.Actors ?? []).Select(a => new VideoDetailActorDto { Id = a.Id, Name = a.Name }).ToList(),
        Images = (video.Images ?? []).Select(i => new VideoDetailImageDto { Id = i.Id, Url = i.Url }).ToList(),
    };
}

public sealed class VideoFeedRoutine(VideoFeed feed, FeedCursors cursors, VideoChangeCoverage coverage, FabDbContext context)
    : IRoutine, ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.videos.changes";
    public string Name => RoutineName;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromMinutes(30);
    public PrdbWork Spends => PrdbWork.CatalogueChanges;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        var key = await context.Installation.Select(i => i.PrdbApiKey).SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(key)) return RunResult.NothingToDo;
        var stored = await cursors.PositionAsync(Feed.VideoChanges, cancellationToken);
        if (stored is null || !await context.VideoChangeState.AnyAsync(cancellationToken))
        {
            var clock = await feed.ReadAsync(key, FeedPosition.TheBeginning, 1, cancellationToken);
            if (clock.ServerTimeUtc is not { } bound) return RunResult.Handled(0);
            await coverage.StartAsync(bound, cancellationToken);
            await cursors.SaveAsync(Feed.VideoChanges, FeedPosition.CaughtUpAt(bound), cancellationToken);
            return RunResult.Handled(0, TimeSpan.FromSeconds(10));
        }
        var page = await feed.ReadAsync(key, stored, ChangeFeedRoutine.APage, cancellationToken);
        var next = page.Next();
        if (next is null) return RunResult.Handled(page.Applied);
        await cursors.SaveAsync(Feed.VideoChanges, next, cancellationToken);
        if (!page.HasMore && page.ServerTimeUtc is { } caughtUp)
            await coverage.CaughtUpAsync(caughtUp, cancellationToken);
        return page.HasMore ? RunResult.Handled(page.Applied, TimeSpan.FromSeconds(10)) : RunResult.Handled(page.Applied);
    }
}
