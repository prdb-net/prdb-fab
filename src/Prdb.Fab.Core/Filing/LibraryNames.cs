using System.Buffers;
using System.Text;

namespace Prdb.Fab.Core.Filing;

/// <summary>
/// The two lossy steps every name in the library goes through: taking out what a
/// filesystem will not carry, and making what is left fit.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0017 adopts `prdb-ordeno`'s rules unchanged, because they were measured
/// rather than reasoned — against a real media server and against an SMB 3.1.1
/// share, with a local ext4 filesystem as the control.
/// </para>
/// <para>
/// What is defended against is the storage rather than the media server, which
/// served every character class it was given. The share is the problem: it
/// accepts the reserved characters and does not store them as written, and the
/// same share mounted without `mapposix` rejects them outright.
/// </para>
/// </remarks>
public static class LibraryNames
{
    /// <summary>
    /// What a single path component may weigh. Bytes rather than characters: the
    /// limit was 255 bytes on ext4 and on the share alike, where 85 CJK
    /// characters fit and 86 did not.
    /// </summary>
    public const int ComponentBudgetBytes = 255;

    /// <summary>
    /// What an entry directory has to leave free for the longest name derived
    /// from it: <c> - [2160p]</c> plus a five-byte extension.
    /// </summary>
    /// <remarks>
    /// A constant rather than the length of the extension actually being filed.
    /// The same video arriving as <c>.mkv</c> and as <c>.mpeg</c> has to produce
    /// one directory, or the second Quality of a video whose title had to be cut
    /// would land in a directory of its own.
    /// </remarks>
    public const int DerivedNameBytes = 15;

    /// <summary>What an entry directory name may weigh, once that room is kept.</summary>
    public const int EntryDirectoryBudgetBytes = ComponentBudgetBytes - DerivedNameBytes;

    private static readonly SearchValues<char> Reserved = SearchValues.Create("<>:\"/\\|?*");

    /// <summary>
    /// One piece of arbitrary text — a title, a site — as a path component may
    /// carry it. Reserved and control characters become spaces rather than
    /// vanishing, so that <c>A/B</c> stays two words, and runs of whitespace then
    /// collapse to one.
    /// </summary>
    /// <remarks>
    /// The result may be empty, where a title was nothing but reserved
    /// characters. Callers name that case themselves rather than putting an
    /// empty component in a path.
    /// </remarks>
    public static string Sanitise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var kept = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            // Surrogates fall through untouched: half of an astral character is
            // neither reserved nor a control character, and the pair is back
            // together by the time anything measures it.
            var replaced = Reserved.Contains(character) || char.IsControl(character)
                ? ' '
                : character;

            if (replaced == ' ' && (kept.Length == 0 || kept[^1] == ' '))
            {
                continue;
            }

            kept.Append(replaced);
        }

        // A leading dot hides the directory from a file manager, from the media
        // server's scanner and from this tool's own walk. A trailing dot or space
        // is what the share stores and other clients disagree about.
        return kept.ToString().Trim().Trim('.').Trim();
    }

    /// <summary>
    /// The same name, cut to fit <paramref name="budgetBytes"/> as UTF-8, and
    /// left in a state a filesystem will carry.
    /// </summary>
    /// <remarks>
    /// The cut falls between runes, never inside one: a component truncated
    /// mid-sequence is not merely ugly, it is a name some of these filesystems
    /// refuse. What the cut exposes is trimmed, because a trailing space or
    /// period is the share problem again and a trailing hyphen is the separator
    /// of a segment that is no longer there.
    /// </remarks>
    public static string Fit(string name, int budgetBytes)
    {
        if (Encoding.UTF8.GetByteCount(name) <= budgetBytes)
        {
            return TrimTail(name);
        }

        var kept = new StringBuilder(name.Length);
        var used = 0;

        foreach (var rune in name.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > budgetBytes)
            {
                break;
            }

            used += rune.Utf8SequenceLength;
            kept.Append(rune.ToString());
        }

        return TrimTail(kept.ToString());
    }

    private static string TrimTail(string name) => name.TrimEnd(' ', '.', '-', '_');
}
