namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>A fresh capacity hint, never a reservation or proof that a write will succeed.</summary>
public static class StorageSpace
{
    public static long? AvailableAt(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var drive = DriveInfo.GetDrives()
                .Where(item => full == Path.TrimEndingDirectorySeparator(item.Name)
                    || full.StartsWith(Path.EndsInDirectorySeparator(item.Name) ? item.Name : item.Name + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .OrderByDescending(item => item.Name.Length)
                .FirstOrDefault();
            return drive?.IsReady == true ? drive.AvailableFreeSpace : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static bool IsInsufficient(long required, long? available) => available is { } bytes && bytes < required;

    public static void RequireCopyCapacity(string target, long bytes)
    {
        if (IsInsufficient(bytes, AvailableAt(target)))
            throw new InsufficientStorageException();
    }
}

public sealed class InsufficientStorageException() : IOException(
    "There is not enough free space on the destination for the verified copy. Free space or extend storage; Filing will retry automatically.");
