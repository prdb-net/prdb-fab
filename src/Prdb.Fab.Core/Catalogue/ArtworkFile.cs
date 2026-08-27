namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// Where a cached image lives, relative to the data directory:
/// <c>artwork/&lt;first two hex of the id&gt;/&lt;id&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Named by the image's id and not by the video's.</strong> ADR 0027
/// compares images by identity rather than by bytes, because the repair pass
/// already diffs <c>images[]</c> and therefore knows when the chosen entry has
/// become a different one. Naming by image id makes that comparison free on
/// this side too: a changed choice is a different filename, the old file simply
/// stops being referenced, and nothing has to decide whether the bytes it finds
/// are current.
/// </para>
/// <para>
/// <strong>Two hex digits of fan-out</strong>, so the cache is 256 directories
/// of a few hundred files rather than one directory of a hundred thousand —
/// which is one nobody can list, back up by hand, or delete from safely.
/// </para>
/// <para>
/// A rule rather than a filesystem call, which is why it is here: it is string
/// arithmetic over an id, and <c>Path</c> is the one thing ADR 0035 lets
/// <c>Core</c> name.
/// </para>
/// </remarks>
public static class ArtworkFile
{
    /// <summary>The one directory the cache occupies under the data directory.</summary>
    public const string Directory = "artwork";

    /// <summary>
    /// The path of the image with this id, under the data directory and in the
    /// platform's own separator.
    /// </summary>
    public static string PathOf(Guid imageId)
    {
        var name = NameOf(imageId);

        return Path.Combine(Directory, name[..2], name);
    }

    /// <summary>
    /// The name the file carries: the id, lower case, without its dashes.
    /// </summary>
    /// <remarks>
    /// One spelling of a UUID, fixed here so that the name is the same whatever
    /// wrote it. The dashes go because they say nothing and the first two
    /// characters are then two hex digits rather than one of eight groups.
    /// </remarks>
    public static string NameOf(Guid imageId) => imageId.ToString("n");
}
