using Microsoft.Extensions.Logging;
using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Scheduling;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Connections;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

public sealed class IgnoredSiteFeed(FabDbContext context, PrdbGateway prdb, CatalogueRows catalogue, IgnoredSites ignores)
    : ChangeFeed(context, prdb, catalogue)
{
    public override Feed Feed => Feed.IgnoredSites;
    public override PrdbWork Work => PrdbWork.UserFeeds;

    public override async Task<FeedPage> ReadAsync(string apiKey, FeedPosition from, int pageSize, CancellationToken cancellationToken)
    {
        var page = await Prdb.AskAsync(apiKey, Work, (client, token) => client.IgnoredSites.Changes.GetAsync(r =>
        {
            r.QueryParameters.Since = from.Since;
            r.QueryParameters.SinceId = from.SinceId;
            r.QueryParameters.PageSize = pageSize;
        }, token), cancellationToken);
        if (page is null) return FeedPage.Nothing;
        var applied = 0;
        foreach (var item in page.Items ?? [])
        {
            if (item.IgnoredSite is not { Id: { } id, UpdatedAtUtc: { } stamp } site) continue;
            await ignores.ApplyAsync(id, site.IsDeleted is not true, stamp, cancellationToken);
            applied++;
        }
        return new(applied, page.HasMore ?? false, page.NextCursor?.UpdatedAtUtc, page.NextCursor?.Id,
            page.ServerTimeUtc, page.Items?.Count ?? 0);
    }
}

public sealed class IgnoredSiteFeedRoutine(IgnoredSiteFeed feed, FeedCursors cursors, FabDbContext context,
    ILogger<IgnoredSiteFeedRoutine> logger) : ChangeFeedRoutine(feed, cursors, context, logger), ISpendsPrdbBudget
{
    public const string RoutineName = "prdb.ignored-sites";
    public override string Name => RoutineName;
    public override TimeSpan Cadence => TimeSpan.FromHours(1);
    public PrdbWork Spends => Source.Work;
}
