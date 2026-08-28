using System.Globalization;
using System.Text;

namespace Prdb.Fab.Core.Filing;

/// <summary>
/// What prdb said about a video at the moment it was filed, which is everything
/// its names are derived from.
/// </summary>
public sealed record FiledVideo(Guid VideoId, string Site, string Title, DateOnly? ReleaseDate);

/// <summary>
/// The names one library entry occupies: the site directory, the entry directory
/// inside it, and the extension the file being filed carries.
/// </summary>
/// <remarks>
/// Nothing here reads a filesystem. The same video and the same metadata always
/// produce the same names, which is what makes a computed path comparable with a
/// recorded one.
/// </remarks>
public sealed record EntryPath(string SiteDirectory, string EntryDirectory, string Extension)
{
    /// <summary>
    /// The sidecar's name, which is the same in every entry directory: a Movies
    /// library reads <c>movie.nfo</c>, and the per-file form collides with the
    /// version grouping.
    /// </summary>
    public const string SidecarFileName = "movie.nfo";

    /// <summary>
    /// The entry image's name. prdb's images have the shape of the video, and a
    /// landscape image in the Primary slot is measurably worse than none, so this
    /// is `fanart.jpg` and there is no poster beside it (ADR 0027).
    /// </summary>
    public const string EntryImageFileName = "fanart.jpg";

    /// <summary>
    /// The names read back off an entry directory this tool filed into earlier,
    /// rather than computed afresh.
    /// </summary>
    /// <remarks>
    /// A second Quality goes next to the first, so its name is derived from the
    /// directory that is <em>there</em> — which may carry a video id from a
    /// broken collision, or be truncated differently from what the layout would
    /// produce for the same video today. Recomputing it would put a file into
    /// that directory whose name does not begin with the directory's own, and the
    /// grouping rule is what that costs: two entries with identical names instead
    /// of one entry with two versions.
    /// </remarks>
    public static EntryPath At(string entryDirectory, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryDirectory);

        var trimmed = entryDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return new EntryPath(
            Path.GetFileName(Path.GetDirectoryName(trimmed)) ?? string.Empty,
            Path.GetFileName(trimmed),
            ExtensionOf(extension));
    }

    /// <summary>The video file, as it is named when there is only one of it.</summary>
    public string VideoFileName => EntryDirectory + Extension;

    /// <summary>
    /// The video file named as one Quality among several. The bracketed form is
    /// not decoration: without it the two files are not grouped, and the library
    /// shows two entries with identical names instead of one with two versions.
    /// </summary>
    public string VideoFileNameFor(string? qualityLabel)
    {
        var label = LibraryNames.Sanitise(qualityLabel);

        return string.IsNullOrEmpty(label)
            ? VideoFileName
            : $"{EntryDirectory} - [{label}]{Extension}";
    }

    /// <summary>The entry directory, below the library root the user configured.</summary>
    public string DirectoryUnder(string libraryRoot) =>
        Path.Combine(libraryRoot, SiteDirectory, EntryDirectory);

    public string VideoFileUnder(string libraryRoot, string? qualityLabel = null) =>
        Path.Combine(DirectoryUnder(libraryRoot), VideoFileNameFor(qualityLabel));

    public string SidecarUnder(string libraryRoot) =>
        Path.Combine(DirectoryUnder(libraryRoot), SidecarFileName);

    public string EntryImageUnder(string libraryRoot) =>
        Path.Combine(DirectoryUnder(libraryRoot), EntryImageFileName);

    /// <summary>
    /// The extension as it will be written. A file arriving without one keeps
    /// none: inventing <c>.mkv</c> would put a name on disk that lies about what
    /// the container is.
    /// </summary>
    internal static string ExtensionOf(string? extension)
    {
        var name = LibraryNames.Sanitise(extension?.TrimStart('.'));

        return string.IsNullOrEmpty(name) ? string.Empty : "." + name.ToLowerInvariant();
    }
}

/// <summary>
/// The layout, as names: <c>&lt;Site&gt;/&lt;Site&gt; - &lt;yyyy-MM-dd&gt; -
/// &lt;Title&gt;/</c>, and the same without the middle segment where prdb knows
/// no release date.
/// </summary>
public static class EntryPaths
{
    /// <summary>
    /// The names for one video. <paramref name="distinguish"/> appends prdb's
    /// video id, which is how a collision is broken — <see cref="FiledPaths"/>
    /// decides when that is called for.
    /// </summary>
    /// <param name="extension">
    /// Taken from the file being filed rather than assumed, since this tool files
    /// what it finds and finds fourteen extensions.
    /// </param>
    public static EntryPath For(FiledVideo video, string? extension, bool distinguish = false)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentException.ThrowIfNullOrWhiteSpace(video.Site);
        ArgumentException.ThrowIfNullOrWhiteSpace(video.Title);

        // A site or title made of nothing but reserved characters sanitises to
        // nothing, and an empty path component is worse than an ugly one.
        var fallback = video.VideoId.ToString("d", CultureInfo.InvariantCulture);
        var site = Or(LibraryNames.Sanitise(video.Site), fallback);
        var title = Or(LibraryNames.Sanitise(video.Title), fallback);
        var date = video.ReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // With no date the segment goes, separator and all. Nothing takes its
        // place: a placeholder is either believed by the media server or is a
        // false-looking name for something that is simply not known.
        var name = date is null
            ? $"{site} - {title}"
            : $"{site} - {date} - {title}";
        var suffix = distinguish ? $" [{video.VideoId:d}]" : string.Empty;

        return new EntryPath(
            LibraryNames.Fit(site, LibraryNames.ComponentBudgetBytes),
            LibraryNames.Fit(
                name,
                LibraryNames.EntryDirectoryBudgetBytes - Encoding.UTF8.GetByteCount(suffix)) + suffix,
            EntryPath.ExtensionOf(extension));
    }

    private static string Or(string name, string fallback) =>
        string.IsNullOrEmpty(name) ? fallback : name;
}
