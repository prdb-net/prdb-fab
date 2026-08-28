using System.Linq.Expressions;

using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.ReleaseDiscovery;

namespace Prdb.Fab.Infrastructure.Acquisition;

/// <summary>A Download keeps the cached Release that supplies its visible size and identity.</summary>
public sealed class DownloadReleasePin(FabDbContext context) : IReleasePin
{
    public Expression<Func<ReleaseRow, bool>> PointsAt =>
        release => context.Downloads.Any(download =>
            download.IndexerId == release.IndexerId
            && download.DerivedReleaseId == release.DerivedReleaseId);
}
