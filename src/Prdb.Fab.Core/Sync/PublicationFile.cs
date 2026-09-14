namespace Prdb.Fab.Core.Sync;

/// <summary>
/// Where a generated preview's bytes live, relative to the data directory:
/// <c>publications/&lt;first two hex of the id&gt;/&lt;id&gt;.jpg</c> and the
/// same name with <c>.vtt</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No version in the name, unlike
/// <see cref="Catalogue.PreviewAssetFile"/>.</strong> That cache holds
/// something prdb may republish under an id it keeps, so the version is what
/// stops two halves from being from different ones. A publication is the
/// opposite: the row <em>is</em> the version — one per file, hash and output
/// version, by the key on the table — so a second version of the same file is a
/// second row with an id of its own, and the id alone cannot mix two of
/// anything.
/// </para>
/// <para>
/// <strong>Its own directory, and disposable in a way the other cache is
/// not.</strong> These bytes exist to be sent; ADR 0064 deletes them the moment
/// an upload reaches a final outcome, and nothing regenerates them afterwards.
/// A file here that no row claims is therefore a crash's leftover rather than a
/// stale copy of anything, which is what the sweep goes looking for.
/// </para>
/// </remarks>
public static class PublicationFile
{
    /// <summary>The one directory generated previews occupy under the data directory.</summary>
    public const string Directory = "publications";

    /// <summary>What a generated sheet's file is called.</summary>
    public const string SheetSuffix = ".jpg";

    /// <summary>What the paired WebVTT's file is called.</summary>
    public const string VttSuffix = ".vtt";

    /// <summary>
    /// What a half-written file is called while it is being written.
    /// </summary>
    /// <remarks>
    /// Read by the sweep rather than by the writer: a part file carries no id
    /// anybody will look for, and a crash mid-write is exactly the case that
    /// leaves one behind.
    /// </remarks>
    public const string PartSuffix = ".part";

    /// <summary>The sheet of this publication, under the data directory.</summary>
    public static string SheetPathOf(Guid publicationId) => PathOf(publicationId, SheetSuffix);

    /// <summary>The WebVTT of this publication, under the data directory.</summary>
    public static string VttPathOf(Guid publicationId) => PathOf(publicationId, VttSuffix);

    /// <summary>
    /// The publication a file belongs to, or null where the name is not one of
    /// ours — a part file included, which belongs to nothing finished.
    /// </summary>
    public static Guid? Read(string fileName)
    {
        var name = fileName;

        if (name.EndsWith(SheetSuffix, StringComparison.Ordinal))
        {
            name = name[..^SheetSuffix.Length];
        }
        else if (name.EndsWith(VttSuffix, StringComparison.Ordinal))
        {
            name = name[..^VttSuffix.Length];
        }
        else
        {
            return null;
        }

        return Guid.TryParseExact(name, "N", out var publicationId) ? publicationId : null;
    }

    private static string PathOf(Guid publicationId, string suffix)
    {
        var name = publicationId.ToString("n");

        return Path.Combine(Directory, name[..2], $"{name}{suffix}");
    }
}
