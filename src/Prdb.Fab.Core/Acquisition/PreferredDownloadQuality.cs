using System.Text.RegularExpressions;

namespace Prdb.Fab.Core.Acquisition;

/// <summary>
/// The highest named Quality a person wants a Catalogue-card Download to
/// choose. Lower named Qualities are fallbacks; higher ones are not.
/// </summary>
public enum PreferredDownloadQuality
{
    P480,
    P720,
    P1080,
    P2160,
}

/// <summary>
/// Applies a person's named Quality ceiling without replacing ADR 0008's
/// ordering inside one Quality. Indexers do not expose a dependable Quality
/// field, so this hint is deliberately limited to common Release-name tags.
/// </summary>
public static partial class PreferredReleaseSelection
{
    public static T? Best<T>(
        IEnumerable<T> releases,
        PreferredDownloadQuality preferred,
        Func<T, string> title)
        where T : class
    {
        var held = releases
            .Select(release => new Candidate<T>(release, QualityOf(title(release))))
            .ToArray();

        foreach (var quality in AtOrBelow(preferred))
        {
            if (held.FirstOrDefault(candidate => candidate.Quality == quality) is { } found)
            {
                return found.Release;
            }
        }

        // Some Indexers omit a recognisable Quality from otherwise usable
        // Releases. Preserve the existing ranking as the honest last resort;
        // a known Quality above the person's ceiling is never substituted.
        return held.FirstOrDefault(candidate => candidate.Quality is null)?.Release;
    }

    public static PreferredDownloadQuality? QualityOf(string title)
    {
        if (P2160().IsMatch(title) || FourK().IsMatch(title) || Uhd().IsMatch(title))
        {
            return PreferredDownloadQuality.P2160;
        }

        if (P1080().IsMatch(title) || Fhd().IsMatch(title)) return PreferredDownloadQuality.P1080;
        if (P720().IsMatch(title)) return PreferredDownloadQuality.P720;
        if (P480().IsMatch(title)) return PreferredDownloadQuality.P480;
        return null;
    }

    private static IEnumerable<PreferredDownloadQuality> AtOrBelow(PreferredDownloadQuality preferred)
    {
        var value = (int)preferred;
        for (var quality = value; quality >= (int)PreferredDownloadQuality.P480; quality--)
        {
            yield return (PreferredDownloadQuality)quality;
        }
    }

    private sealed record Candidate<T>(T Release, PreferredDownloadQuality? Quality);

    [GeneratedRegex(@"(?<![0-9])2160p(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex P2160();

    [GeneratedRegex(@"(?<![0-9])1080p(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex P1080();

    [GeneratedRegex(@"(?<![0-9])720p(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex P720();

    [GeneratedRegex(@"(?<![0-9])480p(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex P480();

    [GeneratedRegex(@"(?<![a-z0-9])4k(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FourK();

    [GeneratedRegex(@"(?<![a-z0-9])uhd(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Uhd();

    [GeneratedRegex(@"(?<![a-z0-9])fhd(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Fhd();
}
