namespace Prdb.Fab.Core.ReleaseDiscovery;

/// <summary>Derives the stable identity Newznab leaves in several different shapes.</summary>
public static class ReleaseIdentity
{
    public static string? From(string? attributeGuid, string? rawGuid)
    {
        if (!string.IsNullOrWhiteSpace(attributeGuid))
        {
            return attributeGuid.Trim();
        }

        var value = rawGuid?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        var segment = uri.Segments.Select(part => part.Trim('/')).LastOrDefault(part => part.Length > 0);
        return string.IsNullOrWhiteSpace(segment) ? value : Uri.UnescapeDataString(segment);
    }
}
