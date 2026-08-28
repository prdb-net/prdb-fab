using System.Linq.Expressions;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Filing;

public sealed class LibraryEntryVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.LibraryEntry;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.LibraryEntries.Any(entry => entry.VideoId == video.PrdbId);

    public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince =>
        video => context.LibraryEntries
            .Where(entry => entry.VideoId == video.PrdbId)
            .Max(entry => (DateTimeOffset?)entry.FiledAt);
}

public sealed class DownloadVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.Download;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.Downloads.Any(download => download.VideoId == video.PrdbId);

    public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince =>
        video => context.Downloads
            .Where(download => download.VideoId == video.PrdbId)
            .Max(download => (DateTimeOffset?)download.CreatedAt);
}

public sealed class ArrivingFileVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.ReviewQueueEntry;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.ArrivingFiles.Any(file => file.VideoId == video.PrdbId);

    public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince =>
        video => context.Downloads
            .Where(download => context.ArrivingFiles.Any(file =>
                file.DownloadId == download.Id && file.VideoId == video.PrdbId))
            .Max(download => (DateTimeOffset?)download.CreatedAt);
}

public sealed class ArrivingFileCandidateVideoPin(FabDbContext context) : ICataloguePin
{
    public PinReason Reason => PinReason.CandidateVideo;

    public Expression<Func<CatalogueVideoRow, bool>> PointsAt =>
        video => context.ArrivingFileCandidates.Any(candidate => candidate.VideoId == video.PrdbId);

    public Expression<Func<CatalogueVideoRow, DateTimeOffset?>> PointedAtSince =>
        video => context.Downloads
            .Where(download => context.ArrivingFileCandidates.Any(candidate =>
                context.ArrivingFiles.Any(file =>
                    file.Id == candidate.ArrivingFileId
                    && file.DownloadId == download.Id
                    && candidate.VideoId == video.PrdbId)))
            .Max(download => (DateTimeOffset?)download.CreatedAt);
}
