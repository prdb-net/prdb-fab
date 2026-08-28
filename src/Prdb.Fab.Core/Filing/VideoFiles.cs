namespace Prdb.Fab.Core.Filing;

/// <summary>Which names Collecting treats as supported Video Files.</summary>
public static class VideoFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".mpg", ".mpeg",
        ".m2ts", ".ts", ".flv", ".webm", ".divx", ".vob",
    };

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eaDir",
        "#recycle",
        "#snapshot",
        ".@__thumb",
        "$RECYCLE.BIN",
        "System Volume Information",
        "lost+found",
    };

    public static bool IsSupported(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !fileName.StartsWith('.')
        && Extensions.Contains(Path.GetExtension(fileName));

    public static bool IsWorthWalking(string directoryName) =>
        !directoryName.StartsWith('.') && !IgnoredDirectories.Contains(directoryName);
}
