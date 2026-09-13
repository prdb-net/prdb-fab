using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// ADR 0061's files: a user preview's sprite and its WebVTT, under the data
/// directory at <c>previews/&lt;two hex&gt;/&lt;id&gt;-&lt;version&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bytes, and nothing about the rows — the same seam
/// <see cref="ArtworkStore"/> draws, for the same reason. What is different is
/// that a thing here is a <em>pair</em>, and the store is what makes writing one
/// safe: each half is written under a temporary name and renamed, and the pair
/// is only ever spoken for once <see cref="Holds"/> says both are there. The row
/// commits the version afterwards, so a crash between the two halves leaves two
/// files nothing claims rather than a preview half of which is last week's.
/// </para>
/// <para>
/// Nothing here is in the Backup. ADR 0009's test is <em>cannot be fetched
/// again</em>, and every file under this directory can — twice over, since half
/// of them may have been withdrawn by the time anybody restores.
/// </para>
/// </remarks>
public sealed class PreviewAssetStore(FabDatabaseLocation location, ILogger<PreviewAssetStore> logger)
{
    private readonly string root = Path.Combine(location.DataDirectory, PreviewAssetFile.Directory);

    /// <summary>Where this preview's image is at this version, whether or not it is there.</summary>
    public string ImagePathOf(Guid previewId, string version) =>
        Path.Combine(location.DataDirectory, PreviewAssetFile.ImagePathOf(previewId, version));

    /// <summary>Where this preview's WebVTT is at this version.</summary>
    public string VttPathOf(Guid previewId, string version) =>
        Path.Combine(location.DataDirectory, PreviewAssetFile.VttPathOf(previewId, version));

    /// <summary>
    /// Whether this version's whole asset is on disk — both halves for a sprite,
    /// the image alone for a single picture.
    /// </summary>
    public bool Holds(Guid previewId, string version, bool paired) =>
        File.Exists(ImagePathOf(previewId, version))
        && (!paired || File.Exists(VttPathOf(previewId, version)));

    public Task WriteImageAsync(Guid previewId, string version, byte[] bytes, CancellationToken cancellationToken) =>
        WriteAsync(ImagePathOf(previewId, version), bytes, cancellationToken);

    public Task WriteVttAsync(Guid previewId, string version, byte[] bytes, CancellationToken cancellationToken) =>
        WriteAsync(VttPathOf(previewId, version), bytes, cancellationToken);

    /// <summary>Opens one half for reading, or null where there is none.</summary>
    public Stream? OpenImage(Guid previewId, string version) => Open(ImagePathOf(previewId, version));

    public Stream? OpenVtt(Guid previewId, string version) => Open(VttPathOf(previewId, version));

    /// <summary>Reads one half whole, or null where there is none.</summary>
    /// <remarks>
    /// The WebVTT is parsed rather than streamed, and it is bounded at a quarter
    /// of a megabyte, so reading it into memory is what it is for. The image is
    /// read whole only when its geometry is being checked.
    /// </remarks>
    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken);
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

    /// <summary>Drops both halves of one version. Silent where there are none.</summary>
    public void Delete(Guid previewId, string version)
    {
        Remove(ImagePathOf(previewId, version));
        Remove(VttPathOf(previewId, version));
    }

    /// <summary>
    /// Every preview and version the cache has a file for, with what each file
    /// weighs.
    /// </summary>
    /// <remarks>
    /// The one place the directory is read rather than the table, and what makes
    /// an orphan findable: a row that has moved to a new version, an expired
    /// interest, a withdrawal. A half-written file is skipped rather than
    /// deleted — this method answers a question, and sweeping is somebody
    /// else's act.
    /// </remarks>
    public IEnumerable<(Guid PreviewId, string Version, long Bytes)> Held()
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (PreviewAssetFile.Read(Path.GetFileName(file)) is not { } named)
            {
                continue;
            }

            long bytes;

            try
            {
                bytes = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                continue;
            }

            yield return (named.PreviewId, named.Version, bytes);
        }
    }

    private static async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Beside the file rather than in the system temporary directory, so
        // that the rename stays within one filesystem (ADR 0034 mounts /data
        // from the host and the container's own /tmp is not on it). The rename
        // is atomic, so a reader finds the whole file or none of it.
        var writing = $"{path}.{Guid.NewGuid():n}.part";

        try
        {
            await File.WriteAllBytesAsync(writing, bytes, cancellationToken);

            File.Move(writing, path, overwrite: true);
        }
        catch
        {
            Forget(writing);
            throw;
        }
    }

    private static Stream? Open(string path)
    {
        try
        {
            return File.OpenRead(path);
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

    private void Remove(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a routine over, and not worth being silent
            // about: a cache that cannot delete is one that will eventually be
            // over its ceiling with nothing saying why.
            logger.LogWarning(failed, "Could not remove a cached user preview asset.");
        }
    }

    private static void Forget(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            // The half-written file of a write that threw. It carries no
            // version anybody will look for and the sweep skips it by name.
        }
    }
}
