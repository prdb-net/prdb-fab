using System.Text;

namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>The one normal form used by discovery and the later screening passes.</summary>
public static class ReleaseTitle
{
    public static string Normalise(string? title)
    {
        var source = (title ?? string.Empty).Trim();
        var dot = source.LastIndexOf('.');

        if (dot > 0 && source.Length - dot is >= 2 and <= 6
            && source[(dot + 1)..].All(char.IsLetterOrDigit))
        {
            source = source[..dot];
        }

        var normalised = new StringBuilder(source.Length);
        var separating = true;

        foreach (var character in source.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                normalised.Append(char.ToLowerInvariant(character));
                separating = false;
            }
            else if (!separating)
            {
                normalised.Append(' ');
                separating = true;
            }
        }

        return normalised.ToString().Trim();
    }
}
