using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0030's files: one image per catalogue image row, under the data
/// directory at <c>artwork/&lt;first two hex of the id&gt;/&lt;id&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bytes, and nothing about the rows — which is the seam that lets the
/// routine, the display path and eviction all say what they mean without any of
/// them touching a stream. <see cref="ArtworkFile"/> in <c>Core</c> is where
/// the path is decided; this is where it is reached.
/// </para>
/// <para>
/// <strong>Written under a temporary name and renamed.</strong> A rename within
/// one directory is atomic on every filesystem this runs on, so a reader either
/// finds the whole image or finds nothing — never the half of one that was on
/// disk when the container was stopped. ADR 0027 noted the same move on the
/// filing side; this is the other end of it.
/// </para>
/// <para>
/// <strong>Nothing here is in the backup.</strong> ADR 0009's test is
/// <em>cannot be fetched again</em>, and every file under this directory can.
/// </para>
/// </remarks>
public sealed class ArtworkStore(FabDatabaseLocation location, ILogger<ArtworkStore> logger)
{
    private readonly string root = Path.Combine(location.DataDirectory, ArtworkFile.Directory);

    /// <summary>Where the image with this id is, whether or not it is there.</summary>
    public string PathOf(Guid imageId) =>
        Path.Combine(location.DataDirectory, ArtworkFile.PathOf(imageId));

    /// <summary>How large the file is, or null where there is none.</summary>
    /// <remarks>
    /// The one question eviction asks of the filesystem, and it asks it of the
    /// file rather than of a column: a size stored beside the row would be
    /// another writer with no reader that would notice it being wrong, which is
    /// the argument ADR 0033 made about the pin.
    /// </remarks>
    public long? SizeOf(Guid imageId)
    {
        var file = new FileInfo(PathOf(imageId));

        return file.Exists ? file.Length : null;
    }

    /// <summary>Whether the bytes are on disk.</summary>
    public bool Holds(Guid imageId) => File.Exists(PathOf(imageId));

    /// <summary>
    /// Puts <paramref name="bytes"/> under the image's id, replacing whatever
    /// was there.
    /// </summary>
    public async Task WriteAsync(Guid imageId, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = PathOf(imageId);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Beside the file rather than in the system temporary directory, so
        // that the rename below stays within one filesystem. ADR 0034 mounts
        // /data from the host and the container's own /tmp is not on it.
        var writing = $"{path}.{Guid.NewGuid():n}.part";

        try
        {
            await File.WriteAllBytesAsync(writing, bytes, cancellationToken);

            File.Move(writing, path, overwrite: true);
        }
        catch
        {
            Remove(writing);
            throw;
        }
    }

    /// <summary>Opens the image for reading, or null where there is none.</summary>
    /// <remarks>
    /// A stream rather than the bytes, because the caller is a response the
    /// framework copies to a socket and there is no reason for a poster to sit
    /// in memory twice on the way there.
    /// </remarks>
    public Stream? Open(Guid imageId)
    {
        try
        {
            return File.OpenRead(PathOf(imageId));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Drops the image's file. Silent where there is none: eviction and a
    /// removed row both arrive here, and neither is wrong for finding the work
    /// already done.
    /// </summary>
    public void Delete(Guid imageId) => Remove(PathOf(imageId));

    /// <summary>
    /// Every image id the cache has bytes for, whether or not a row still names
    /// it.
    /// </summary>
    /// <remarks>
    /// The one place the directory is read rather than the table, and it is
    /// what makes an orphan findable: a catalogue row evicted by
    /// <see cref="CatalogueEviction"/> takes its image rows with it by cascade
    /// (ADR 0033), and the bytes it leaves are named by an id nothing points at
    /// any more. Two hex digits of fan-out is what keeps this affordable — 256
    /// directories of a few hundred entries.
    /// </remarks>
    public IEnumerable<Guid> Held()
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);

            // The half-written files of an interrupted WriteAsync, and anything
            // else that is not an id. Skipped rather than deleted: this method
            // answers a question, and sweeping is somebody else's act.
            if (Guid.TryParseExact(name, "N", out var imageId))
            {
                yield return imageId;
            }
        }
    }

    private void Remove(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException failed)
        {
            // Not worth failing a routine over, and not worth being silent
            // about either: a cache that cannot delete is a cache that will
            // eventually be over its ceiling with nothing saying why.
            logger.LogWarning(failed, "Could not remove a cached image from the artwork cache.");
        }
        catch (UnauthorizedAccessException failed)
        {
            logger.LogWarning(failed, "Could not remove a cached image from the artwork cache.");
        }
    }
}
