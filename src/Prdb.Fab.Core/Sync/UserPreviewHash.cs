namespace Prdb.Fab.Core.Sync;

/// <summary>
/// The one spelling of an osHash this tool compares by.
/// </summary>
/// <remarks>
/// <para>
/// Two populations meet on this value and neither owns it. A file's hash is
/// computed locally by <c>Prdb.Hashing</c>; a user preview's arrives in a prdb
/// payload as whatever the submitter's client sent, under a pattern that admits
/// both cases (<c>^[0-9A-Fa-f]{16}$</c>). Comparing them as they come would make
/// an automatic Identification depend on somebody else's shift key, which is the
/// kind of bug that is invisible until it is a support question.
/// </para>
/// <para>
/// Upper case because that is the direction prdb's own document normalises in —
/// it says perceptual hashes are stored and returned uppercase — and because a
/// hash beside a hash in a log should look the same either way round.
/// </para>
/// </remarks>
public static class UserPreviewHash
{
    /// <summary>How many characters an osHash is.</summary>
    public const int Length = 16;

    /// <summary>
    /// The comparable form of <paramref name="hash"/>, or null where it is not
    /// an osHash at all.
    /// </summary>
    /// <remarks>
    /// A refusal rather than a best effort. Something that is not sixteen
    /// hexadecimal characters is not a hash somebody typed slightly wrong; it is
    /// a value from somewhere this code did not expect, and matching it against
    /// a file would be worse than matching nothing.
    /// </remarks>
    public static string? Normalise(string? hash)
    {
        var trimmed = (hash ?? string.Empty).Trim();

        if (trimmed.Length != Length)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (!Uri.IsHexDigit(character))
            {
                return null;
            }
        }

        return trimmed.ToUpperInvariant();
    }
}
