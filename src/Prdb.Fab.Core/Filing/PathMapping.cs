namespace Prdb.Fab.Core.Filing;

/// <summary>Resolves a path from SABnzbd's filesystem view into this container.</summary>
public static class PathMapping
{
    public static string? Resolve(string? from, string? to, string? reported)
    {
        if (string.IsNullOrWhiteSpace(from)
            || string.IsNullOrWhiteSpace(to)
            || string.IsNullOrWhiteSpace(reported))
        {
            return null;
        }

        var remoteRoot = TrimSeparators(from.Trim());
        var remotePath = TrimSeparators(reported.Trim());
        var comparison = LooksLikeWindows(remoteRoot) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!remotePath.StartsWith(remoteRoot, comparison))
        {
            return null;
        }

        if (remotePath.Length > remoteRoot.Length
            && !IsSeparator(remoteRoot[^1])
            && !IsSeparator(remotePath[remoteRoot.Length]))
        {
            return null;
        }

        var rest = remotePath[remoteRoot.Length..].TrimStart('/', '\\');
        var segments = rest.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return Path.GetFullPath(segments.Aggregate(to.Trim(), Path.Combine));
    }

    private static string TrimSeparators(string path) =>
        path.Length <= 1 ? path : path.TrimEnd('/', '\\');

    private static bool LooksLikeWindows(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal)
        || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');

    private static bool IsSeparator(char value) => value is '/' or '\\';
}
