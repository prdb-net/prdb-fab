using System.Linq.Expressions;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>One indexed query source that makes a cached Release non-disposable.</summary>
public interface IReleasePin
{
    Expression<Func<ReleaseRow, bool>> PointsAt { get; }
}

/// <summary>A Release identified as a video that is still Wanted.</summary>
public sealed class WantedIdentificationReleasePin(FabDbContext context) : IReleasePin
{
    public Expression<Func<ReleaseRow, bool>> PointsAt =>
        release => release.VideoId != null
                   && context.WantedVideos.Any(wanted => wanted.VideoId == release.VideoId);
}
