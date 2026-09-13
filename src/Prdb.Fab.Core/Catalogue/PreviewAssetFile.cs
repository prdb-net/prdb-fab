namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// Where a cached user preview lives, relative to the data directory:
/// <c>previews/&lt;first two hex of the id&gt;/&lt;id&gt;-&lt;version&gt;.jpg</c>
/// and the same name with <c>.vtt</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The version is in the file name, and that is the whole trick.</strong>
/// A user preview is mutable in a way an <c>images[]</c> entry is not: prdb may
/// republish the sprite at a different URL or cut it into a different grid, and
/// the id stays the same through it. Naming by the id alone would mean the two
/// halves of a pair could be from different versions for as long as it takes to
/// fetch the second one — a sheet cut by somebody else's WebVTT, which is worse
/// than no preview at all. With the version in the name the two halves cannot
/// be mixed, the commit is a single row write, and yesterday's version is an
/// orphan the sweep finds rather than something anybody has to delete in the
/// right order.
/// </para>
/// <para>
/// <strong>Its own directory rather than a corner of <c>artwork/</c>.</strong>
/// The two caches have their own ceilings (ADR 0061), and a sweep that reads the
/// disk to find out how full it is has to be able to tell them apart by looking.
/// </para>
/// <para>
/// Two hex digits of fan-out, for ADR 0030's reason: 256 directories of a few
/// hundred files rather than one nobody can list, back up by hand or delete
/// from safely.
/// </para>
/// </remarks>
public static class PreviewAssetFile
{
    /// <summary>The one directory this cache occupies under the data directory.</summary>
    public const string Directory = "previews";

    /// <summary>What a sprite sheet's file is called.</summary>
    public const string ImageSuffix = ".jpg";

    /// <summary>What the paired WebVTT's file is called.</summary>
    public const string VttSuffix = ".vtt";

    /// <summary>The image of this preview at this version, under the data directory.</summary>
    public static string ImagePathOf(Guid previewId, string version) =>
        PathOf(previewId, version, ImageSuffix);

    /// <summary>The WebVTT of this preview at this version, under the data directory.</summary>
    public static string VttPathOf(Guid previewId, string version) =>
        PathOf(previewId, version, VttSuffix);

    /// <summary>
    /// The preview and version a cached file belongs to, or null where the name
    /// is not one of ours.
    /// </summary>
    /// <remarks>
    /// The other direction, and what makes an orphan findable: the sweep walks
    /// the directory and asks each name who it belongs to, because a version
    /// nothing claims any more is exactly a file no row names.
    /// </remarks>
    public static (Guid PreviewId, string Version)? Read(string fileName)
    {
        var name = fileName;

        if (name.EndsWith(ImageSuffix, StringComparison.Ordinal))
        {
            name = name[..^ImageSuffix.Length];
        }
        else if (name.EndsWith(VttSuffix, StringComparison.Ordinal))
        {
            name = name[..^VttSuffix.Length];
        }
        else
        {
            return null;
        }

        var separator = name.IndexOf('-', StringComparison.Ordinal);

        if (separator < 0 || !Guid.TryParseExact(name[..separator], "N", out var previewId))
        {
            return null;
        }

        var version = name[(separator + 1)..];

        return version.Length == 0 ? null : (previewId, version);
    }

    private static string PathOf(Guid previewId, string version, string suffix)
    {
        var name = previewId.ToString("n");

        return Path.Combine(Directory, name[..2], $"{name}-{version}{suffix}");
    }
}
