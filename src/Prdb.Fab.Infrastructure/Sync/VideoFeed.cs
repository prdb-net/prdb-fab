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

    public override Task<FeedPage> ReadAsync(string apiKey, FeedPosition from, int pageSize, CancellationToken cancellationToken) =>
        ReadCoreAsync(apiKey, from, pageSize, checkpoint: false, cancellationToken);

    public Task<FeedPage> ReadAndSaveAsync(string apiKey, FeedPosition from, CancellationToken token) =>
        ReadCoreAsync(apiKey, from, ChangeFeedRoutine.APage, checkpoint: true, token);

    private async Task<FeedPage> ReadCoreAsync(string apiKey, FeedPosition from, int pageSize, bool checkpoint, CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(apiKey, Work, (client, token) => client.Videos.Changes.GetAsync(r =>
        {
            r.QueryParameters.Since = from.Since;
            r.QueryParameters.SinceId = from.SinceId;
            r.QueryParameters.PageSize = pageSize;
        }, token), cancellationToken);
        if (page is null) return FeedPage.Nothing;
        var position = new FeedPage(0, page.HasMore ?? false, page.NextCursor?.UpdatedAtUtc,
            page.NextCursor?.Id, page.ServerTimeUtc, page.Items?.Count ?? 0);
        if (pageSize == 1 && from == FeedPosition.TheBeginning) return position;
        var next = position.Next();
        if (checkpoint && (page.Items is null || page.HasMore is null || next is null || next.At < from.At
            || page.Items.Any(i => i.Video?.Id is null || i.Video.UpdatedAtUtc is null)))
            throw new InvalidOperationException("prdb returned an incomplete or backwards Video change page.");

        // Merge survivors can lie outside the creation range. Resolve known
        // identities before opening the local transaction; every escape is governed.
        var survivorIds = (page.Items ?? []).Where(i => i.Video?.MergedIntoId is not null)
            .Select(i => i.Video!).ToArray();
        var known = await Context.CatalogueVideos.Where(v => survivorIds.Select(i => i.Id).Contains(v.PrdbId))
            .Select(v => v.PrdbId).ToHashSetAsync(cancellationToken);
        // A page may put an intermediate tombstone before its known source.
        // Compute the bounded closure before applying either event.
        bool extended;
        do
        {
            extended = false;
            foreach (var video in survivorIds)
                if (video.Id is { } source && known.Contains(source) && video.MergedIntoId is { } destination && destination != Guid.Empty)
                    extended |= known.Add(destination);
        } while (extended);
        var targets = survivorIds.Where(v => v.Id is { } id && known.Contains(id))
            .Select(v => v.MergedIntoId).OfType<Guid>().Where(id => id != Guid.Empty).Distinct().ToArray();
        var terminalTargets = targets.Where(id => !survivorIds.Any(v => v.Id == id)).ToArray();
        if (targets.Length > 0 && terminalTargets.Length == 0)
            throw new InvalidOperationException("prdb returned a merge cycle without a survivor.");
        var survivors = new List<VideoDetailDto>();
        foreach (var batch in terminalTargets.Chunk(CatalogueRead.ABatch))
        {
            var found = await Prdb.AskAsync(apiKey, Work, (client, ct) => client.Videos.Batch.PostAsync(
                new GetVideosByIdsRequest { Ids = [.. batch.Select(id => (Guid?)id)] }, cancellationToken: ct), cancellationToken);
            survivors.AddRange(found ?? []);
        }
        if (terminalTargets.Except(survivors.Select(v => v.Id).OfType<Guid>()).Any())
            throw new InvalidOperationException("prdb omitted a merge survivor; the change page remains due.");
        await using var applying = await Context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var tombstone in survivorIds.Where(v => v.Id is { } id && known.Contains(id)))
            await Catalogue.VideoAsync(tombstone.Id!.Value, null, null, cancellationToken);
        foreach (var survivor in survivors) await details.WriteAsync(survivor, cancellationToken);
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
        if (checkpoint)
        {
            await new FeedCursors(Context, time).SaveAsync(Feed.VideoChanges, next!, cancellationToken);
            if (!page.HasMore!.Value && page.ServerTimeUtc is { } caughtUp && caughtUp >= from.At)
            {
                await new VideoChangeCoverage(Context, time).CaughtUpAsync(caughtUp, cancellationToken);
                var baseline = await Context.RecentWindowState.AsTracking().SingleAsync(cancellationToken);
                if (baseline.BaselinePhase == "catch-up")
                {
                    baseline.BaselinePhase = "live";
                    baseline.CatalogueCompletedAt = time.GetUtcNow();
                    baseline.CataloguePassStartedAt = null;
                    baseline.CatalogueResumePage = 1;
                    await Context.SaveChangesAsync(cancellationToken);
                }
            }
        }
        await applying.CommitAsync(cancellationToken);
        return position with { Applied = applied };
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

public sealed class VideoFeedRoutine(VideoFeed feed, FeedCursors cursors, VideoChangeCoverage coverage, FabDbContext context, TimeProvider time, VideoSyncGate sync)
    : IRoutine, ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.videos.changes";
    public string Name => RoutineName;
    public Lane Lane => Lane.Sync;
    public TimeSpan Cadence => TimeSpan.FromMinutes(30);
    public PrdbWork Spends => PrdbWork.CatalogueChanges;

    public async Task<RunResult> RunAsync(string? target, CancellationToken cancellationToken)
    {
        await sync.Gate.WaitAsync(cancellationToken);
        try { return await RunCoreAsync(cancellationToken); }
        finally { sync.Gate.Release(); }
    }

    private async Task<RunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var baseline = await context.RecentWindowState.AsTracking().SingleAsync(cancellationToken);
        if (baseline.BaselinePhase == "scan") return RunResult.NothingToDo;
        var key = await context.Installation.Select(i => i.PrdbApiKey).SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(key)) return RunResult.NothingToDo;
        var stored = await cursors.PositionAsync(Feed.VideoChanges, cancellationToken);
        // Tombstones are guaranteed for at least ninety days. Beyond that,
        // an empty resumed page cannot prove the intervening history complete.
        if (stored is null || stored.At < time.GetUtcNow() - TimeSpan.FromDays(90)
            || !await context.VideoChangeState.AnyAsync(cancellationToken))
        {
            if (baseline.BaselinePhase is "live" or "catch-up")
            {
                baseline.BaselinePhase = null;
                baseline.CatalogueCompletedAt = null;
                await context.VideoChangeState.ExecuteUpdateAsync(u => u.SetProperty(v => v.BaselineCompletedAt, (DateTimeOffset?)null), cancellationToken);
                await context.Routines.Where(r => r.Name == RecentCatalogueRoutine.RoutineName)
                    .ExecuteUpdateAsync(u => u.SetProperty(r => r.DueAt, time.GetUtcNow()), cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return RunResult.Handled(0, TimeSpan.Zero);
            }
            var clock = await feed.ReadAsync(key, FeedPosition.TheBeginning, 1, cancellationToken);
            if (clock.ServerTimeUtc is not { } bound) return RunResult.Handled(0);
            await using var starting = await context.Database.BeginTransactionAsync(cancellationToken);
            await coverage.StartAsync(bound, cancellationToken);
            await cursors.SaveAsync(Feed.VideoChanges, FeedPosition.CaughtUpAt(bound), cancellationToken);
            await starting.CommitAsync(cancellationToken);
            return RunResult.Handled(0, TimeSpan.FromSeconds(10));
        }
        var page = await feed.ReadAndSaveAsync(key, stored, cancellationToken);
        return page.HasMore ? RunResult.Handled(page.Applied, TimeSpan.FromSeconds(10)) : RunResult.Handled(page.Applied);
    }
}
