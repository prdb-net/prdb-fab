namespace Prdb.Fab.Core.Filing;

/// <summary>The fixed, non-configurable set of names Tidy-up may delete.</summary>
public static class Leftovers
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".nfo", ".par2", ".sfv", ".srr", ".url", ".txt", ".jpg", ".png",
    };

    public static bool IsSupported(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && Extensions.Contains(Path.GetExtension(fileName));
}
