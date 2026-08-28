using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Infrastructure.Persistence;
using Prdb.Fab.Infrastructure.Sync;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>
/// Writes the sidecar and the entry image into an entry directory, from the
/// catalogue and the artwork cache.
/// </summary>
/// <remarks>
/// <para>
/// Both are written to a dotted temporary name in the same directory, flushed,
/// and renamed into place. The reason is ours rather than the media server's: a
/// container killed halfway through a truncating write leaves a document that
/// parses nowhere, and an unparseable sidecar is discarded in silence. For the
/// image it is one step stronger — a half-written `fanart.jpg` is not merely a
/// bad image, it is a file at the name the next write would otherwise use.
/// </para>
/// <para>
/// Neither file carries a marker and both are replaced unconditionally. The
/// library is the only directory this tool owns, an entry directory exists only
/// because this tool made it, and the sidecar is the single route a correction
/// from prdb has to the user (ADR 0027).
/// </para>
/// <para>
/// The image is copied out of the artwork cache and never fetched here. The file
/// lane holds hour-long moves and waits on nothing remote; where the cache does
/// not hold the image yet, none is written — no wait, no failure, no retry, and
/// the repair pass brings it later.
/// </para>
/// </remarks>
public sealed class EntryFiles(
    FabDbContext context,
    ArtworkStore artwork,
    ILogger<EntryFiles> logger)
{
    public async Task WriteAsync(
        string entryDirectory,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryDirectory);

        var metadata = await MetadataAsync(videoId, cancellationToken);

        if (metadata is null)
        {
            // The catalogue row is written and pinned before an arriving file
            // reaches AwaitingFiling, so this is a caller out of order rather
            // than a video prdb says nothing about.
            throw new InvalidOperationException(
                "The catalogue holds no video for the entry being written.");
        }

        await ReplaceAsync(
            Path.Combine(entryDirectory, EntryPath.SidecarFileName),
            async (stream, token) =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(Sidecar.For(metadata));
                await stream.WriteAsync(bytes, token);
            },
            cancellationToken);

        await WriteImageAsync(entryDirectory, videoId, cancellationToken);
    }

    private async Task WriteImageAsync(
        string entryDirectory,
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var image = await ChosenImageAsync(videoId, cancellationToken);

        if (image is null || !artwork.Holds(image.Value))
        {
            // The same clean entry a video with no artwork at all produces.
            logger.LogInformation(
                "No cached entry image was available while filing, so none was written.");
            return;
        }

        await using var bytes = artwork.Open(image.Value);

        if (bytes is null)
        {
            return;
        }

        await ReplaceAsync(
            Path.Combine(entryDirectory, EntryPath.EntryImageFileName),
            async (stream, token) => await bytes.CopyToAsync(stream, token),
            cancellationToken);
    }

    /// <summary>
    /// Writes one file through a dotted temporary name beside it, so that what a
    /// scanner can see is either the previous file or the whole new one.
    /// </summary>
    private static async Task ReplaceAsync(
        string path,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():n}.part");

        Directory.CreateDirectory(directory);

        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await write(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The leftover is dotted and carries no video container extension,
            // so neither the scanner nor this tool's own walk will read it.
        }
    }

    private async Task<SidecarMetadata?> MetadataAsync(
        Guid videoId,
        CancellationToken cancellationToken)
    {
        var video = await context.CatalogueVideos
            .AsNoTracking()
            .Where(row => row.PrdbId == videoId)
            .Select(row => new
            {
                row.Id,
                row.Title,
                row.ReleaseDate,
                Studio = context.CatalogueSites
                    .Where(site => site.Id == row.SiteId)
                    .Select(site => site.Title)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (video is null)
        {
            return null;
        }

        // The catalogue holds no credited order, so the sidecar states the names
        // in one that is at least the same on every write.
        var actors = await context.CatalogueVideoActors
            .AsNoTracking()
            .Where(link => link.VideoId == video.Id)
            .Join(
                context.CatalogueActors,
                link => link.ActorId,
                actor => actor.Id,
                (link, actor) => actor.Name)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        return new SidecarMetadata(
            videoId,
            video.Title ?? string.Empty,
            video.Studio,
            video.ReleaseDate,
            actors);
    }

    private async Task<Guid?> ChosenImageAsync(Guid videoId, CancellationToken cancellationToken)
    {
        var video = await context.CatalogueVideos
            .AsNoTracking()
            .Where(row => row.PrdbId == videoId)
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (video is null)
        {
            return null;
        }

        var image = await ChosenImages.OfAsync(context, video.Value, cancellationToken);

        return image is null || image.FoundDead || !image.Cached ? null : image.PrdbId;
    }
}
