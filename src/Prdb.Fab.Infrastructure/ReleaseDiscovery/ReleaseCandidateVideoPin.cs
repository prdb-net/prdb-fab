using System.Linq.Expressions;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Keeps every Candidate video while its cached Release remains open.</summary>
public sealed class ReleaseCandidateVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.CachedRelease;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.ReleaseCandidates.Any(candidate => candidate.VideoId == video.Id);

    public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince =>
        video => context.ReleaseCandidates
            .Where(candidate => candidate.VideoId == video.Id)
            .Max(candidate => (DateTimeOffset?)candidate.Release!.FirstSeenAt);
}
