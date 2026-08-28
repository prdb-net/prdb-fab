using System.Text;

namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The title form sent to an indexer's Wanted Sweep search.</summary>
/// <remarks>
/// Deliberately distinct from the local Screening form: this is read by an
/// indexer's tokeniser, while Screening compares two values this tool owns.
/// </remarks>
public static class WantedSearchTitle
{
    public static string Of(string? title)
    {
        var query = new StringBuilder(title?.Length ?? 0);
        var separating = true;

        foreach (var character in title ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                query.Append(character);
                separating = false;
            }
            else if (!separating)
            {
                query.Append(' ');
                separating = true;
            }
        }

        return query.ToString().Trim();
    }

    public static bool IsSearchable(string query)
    {
        var wordsWithLetters = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(word => word.Any(char.IsLetter));
        var characters = query.Count(character => !char.IsWhiteSpace(character));

        return wordsWithLetters >= 2 && characters >= 4;
    }
}
