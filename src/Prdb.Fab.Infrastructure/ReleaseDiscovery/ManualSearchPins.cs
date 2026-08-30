using System.Linq.Expressions;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

public sealed class ManualSearchVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.ManualSearch;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.ManualSearches.Any(search => search.VideoId == video.Id);

    public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince =>
        video => context.ManualSearches
            .Where(search => search.VideoId == video.Id)
            .Max(search => (DateTimeOffset?)search.RequestedAt);
}

public sealed class ManualSearchReleasePin(FabDbContext context) : IReleasePin
{
    public Expression<Func<ReleaseRow, bool>> PointsAt =>
        release => context.ManualSearchResults.Any(result => result.ReleaseId == release.Id);
}
