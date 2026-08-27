using System.Text;

namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// ADR 0023's comparison form: lower case, every separator collapsed to one,
/// the extension dropped.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0025 stores this form beside every title and every pre-name the
/// catalogue holds, and the reason is this class rather than the column: the
/// same function writes the needle and the haystack, so the two cannot drift
/// apart. Two normalisers drifting is ADR 0015's silently skipped row arriving
/// by a different door — nothing fails, a release simply stops matching the
/// video it belongs to.
/// </para>
/// <para>
/// Deliberately <em>not</em> the form ADR 0024 builds an indexer query from.
/// Those two look alike and answer to different masters: that one goes over the
/// wire to be read by somebody else's tokeniser, and this one stays here and is
/// only ever compared with itself.
/// </para>
/// </remarks>
public static class ComparisonForm
{
    /// <summary>
    /// The longest run of letters that is still read as a file extension.
    /// </summary>
    /// <remarks>
    /// Four, which covers <c>webm</c> and everything shorter. It has to start
    /// with a letter — <c>mp4</c> does and the <c>.15</c> of a
    /// <c>site.26.08.15</c> does not, which is the difference between dropping
    /// an extension and shortening every scene release title that ends in a
    /// date.
    /// </remarks>
    private const int LongestExtension = 4;

    /// <summary>
    /// <paramref name="text"/> as everything here compares it. Never null: a
    /// row with no title to compare has an empty comparison form rather than a
    /// missing one, and the columns holding these are required.
    /// </summary>
    public static string Of(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = WithoutExtension(text.Trim());
        var form = new StringBuilder(trimmed.Length);

        foreach (var character in trimmed)
        {
            if (char.IsLetterOrDigit(character))
            {
                form.Append(char.ToLowerInvariant(character));
            }
            else if (form.Length > 0 && form[^1] != ' ')
            {
                // Every separator is one space, whatever it was. A release name
                // is dots, an underscore or two and the occasional bracket, and
                // which of them an indexer chose says nothing about the title
                // underneath.
                form.Append(' ');
            }
        }

        return form.ToString().TrimEnd();
    }

    private static string WithoutExtension(string text)
    {
        var dot = text.LastIndexOf('.');

        if (dot <= 0
            || text.Length - dot - 1 is < 1 or > LongestExtension
            || !char.IsLetter(text[dot + 1]))
        {
            return text;
        }

        for (var index = dot + 2; index < text.Length; index++)
        {
            if (!char.IsLetterOrDigit(text[index]))
            {
                return text;
            }
        }

        return text[..dot];
    }
}
