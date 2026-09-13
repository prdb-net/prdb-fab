using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Prdb.Fab.Core.Sync;

/// <summary>
/// What makes one version of a user preview's asset different from another, and
/// what a sprite sheet has to say about itself before its bytes are worth
/// fetching.
/// </summary>
/// <remarks>
/// ADR 0061 makes the pair — the sprite and its WebVTT — one asset with one
/// version, and this is where that version is decided. Everything that could
/// change what the bytes mean is in it, and nothing else is: two installations
/// looking at the same published preview compute the same token, and a row whose
/// URL or grid has moved computes a different one.
/// </remarks>
public static class UserPreviewAsset
{
    /// <summary>prdb's word for a preview that is a single picture.</summary>
    public const string Single = "Single";

    /// <summary>prdb's word for a preview that is a grid of timed tiles.</summary>
    public const string SpriteSheet = "SpriteSheet";

    /// <summary>
    /// Whether this is a kind of preview this build knows how to show.
    /// </summary>
    /// <remarks>
    /// The upload form's pattern fixes the two words and their
    /// case-insensitivity, and nothing promises the list will not grow. A kind
    /// this build does not know is a preview it does not show — decided here,
    /// deliberately, rather than by a parse that fails into whichever value
    /// happens to be first.
    /// </remarks>
    public static bool IsKnown(string? kind) => IsSingle(kind) || IsSprite(kind);

    public static bool IsSingle(string? kind) =>
        string.Equals(kind, Single, StringComparison.OrdinalIgnoreCase);

    public static bool IsSprite(string? kind) =>
        string.Equals(kind, SpriteSheet, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The version token for one preview's asset: twelve hexadecimal characters
    /// over everything that decides what its bytes are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived rather than counted, so that it needs no writer, no clock and no
    /// coordination — two processes computing it from the same row agree, and a
    /// row restored from a Backup computes what it computed before.
    /// </para>
    /// <para>
    /// Twelve characters is forty-eight bits. What it has to be safe against is
    /// a collision between two versions <em>of the same preview</em>, which is a
    /// handful of values over the life of a row, and the cost of one would be a
    /// stale sheet rather than anything unsafe. A full digest in a file name
    /// buys nothing against that.
    /// </para>
    /// </remarks>
    public static string VersionOf(
        string? url,
        string? vttUrl,
        int? tileCount,
        int? columns,
        int? rows,
        int? tileWidth,
        int? tileHeight)
    {
        var of = string.Join(
            '|',
            url ?? string.Empty,
            vttUrl ?? string.Empty,
            Number(tileCount),
            Number(columns),
            Number(rows),
            Number(tileWidth),
            Number(tileHeight));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(of)))[..12];

        static string Number(int? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
