using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>Full baseline and subsequent Site states, never page omission as deletion.</summary>
public sealed class SiteFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.SiteChanges;
    public override PrdbWork Work => PrdbWork.Sites;

    public override async Task<FeedPage> ReadAsync(string apiKey, FeedPosition from, int pageSize, CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(apiKey, Work, (client, token) => client.Sites.Changes.GetAsync(r =>
        {
            r.QueryParameters.Since = from.Since;
            r.QueryParameters.SinceId = from.SinceId;
            r.QueryParameters.PageSize = pageSize;
        }, token), cancellationToken);
        if (page is null) return FeedPage.Nothing;
        var applied = 0;
        foreach (var item in page.Items ?? [])
        {
            if (item.Site is not { Id: { } id, UpdatedAtUtc: { } stamp } site) continue;
            var row = await Context.CatalogueSites.AsTracking().SingleOrDefaultAsync(s => s.PrdbId == id, cancellationToken);
            if (row?.UpdatedAtUtc is { } held && held > stamp) continue;
            var deleted = site.IsDeleted is true || string.Equals(item.EventType, "deleted", StringComparison.OrdinalIgnoreCase);
            if (row is null)
            {
                row = new CatalogueSiteRow { PrdbId = id, Title = site.Title ?? string.Empty };
                Context.CatalogueSites.Add(row);
            }
            row.UpdatedAtUtc = stamp;
            row.StillOffered = !deleted;
            if (!deleted)
            {
                row.Title = site.Title ?? string.Empty;
                row.Network = site.NetworkTitle;
                row.Url = site.Url;
                row.LinksJson = System.Text.Json.JsonSerializer.Serialize((site.Links ?? []).Select(link => new CatalogueLink(link.Label, link.Url)));
                row.PlatformId = site.PlatformId;
                row.PlatformKey = site.PlatformKey;
                row.PlatformTitle = site.PlatformTitle;
                row.AccountHandle = site.AccountHandle;
            }
            await Context.SaveChangesAsync(cancellationToken);
            applied++;
        }
        return new(applied, page.HasMore ?? false, page.NextCursor?.UpdatedAtUtc, page.NextCursor?.Id,
            page.ServerTimeUtc, page.Items?.Count ?? 0);
    }
}

public sealed class SiteFeedRoutine(SiteFeed feed, FeedCursors cursors, FabDbContext context,
    IRoutineStore routines, ILogger<SiteFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.sites.changes";
    public override string Name => RoutineName;
    public override TimeSpan Cadence => TimeSpan.FromHours(1);
    public PrdbWork Spends => Source.Work;

    protected override async Task ReachedAsync(FeedPosition position, FeedPage page, CancellationToken cancellationToken)
    {
        if (page.HasMore) return;
        await Cursors.SaveAsync(Feed.SiteChangesBaseline, position, cancellationToken);
        await routines.RetireAsync(SiteListRoutine.RoutineName, null, cancellationToken);
    }
}
