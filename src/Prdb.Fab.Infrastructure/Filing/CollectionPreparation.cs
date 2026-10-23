using Microsoft.EntityFrameworkCore;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Filing;

/// <summary>Starts explicit collection work. Reading a page never starts a scan.</summary>
public sealed class CollectionPreparation(FabDbContext context, TimeProvider time)
{
    public async Task<CollectionAct> StartAsync(IReadOnlyCollection<string> directories,
        CancellationToken cancellationToken = default)
    {
        var installation = await context.Installation.SingleAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(installation.LibraryRoot))
            return new(false, "Configure the Library root first.");
        if (await context.CollectionPreparations.AnyAsync(cancellationToken))
            return new(false, "Collection preparation already exists. Continue its current work.");

        var library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation.LibraryRoot));
        var roots = new List<string> { library };
        foreach (var raw in directories.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!Path.IsPathFullyQualified(raw)) return new(false, "Scan directories must be absolute paths.");
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            if (!roots.Contains(path, StringComparer.Ordinal)) roots.Add(path);
        }
        if (roots.Count > 20) return new(false, "Choose at most 19 additional Scan Directories.");
        foreach (var path in roots)
        {
            if (installation.PathMappingTo is { Length: > 0 } downloads && CollectionPaths.Within(path, downloads))
                return new(false, "The SABnzbd download directory cannot be a Scan Directory.");
            try
            {
                CollectionPaths.RequireDirectory(path, path);
                using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                _ = entries.MoveNext();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new(false, "A Scan Directory is missing, unreadable or a symbolic link.");
            }
        }

        // The Library is a separate root even when an additional root contains
        // it. Enumeration does not cross into another configured root.
        roots = roots.Where(path => path == library || !roots.Any(other =>
            other != path && other != library && CollectionPaths.Within(path, other))).ToList();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.CollectionPreparations.Add(new() { StartedAt = time.GetUtcNow() });
        foreach (var path in roots)
        {
            var id = Guid.CreateVersion7(time.GetUtcNow());
            context.CollectionRoots.Add(new() { Id = id, Path = path, IsLibrary = path == library });
            context.CollectionWalks.Add(new() { Path = path, RootId = id });
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, "The existing collection is being inventoried. No files move before approval.");
    }
}

public sealed record CollectionAct(bool Accepted, string Detail);

internal static class CollectionPaths
{
    public static bool Within(string path, string root)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(path, normalized, StringComparison.Ordinal)
            || path.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public static void RequireDirectory(string path, string root)
    {
        RequireContained(path, root);
        if (!File.GetAttributes(path).HasFlag(FileAttributes.Directory))
            throw new IOException("A scan directory is no longer a directory.");
    }

    public static void RequireContained(string path, string root)
    {
        if (!Within(path, root)) throw new IOException("The source is outside its Scan Directory.");
        for (var here = path; ; here = Path.GetDirectoryName(here)!)
        {
            if (File.GetAttributes(here).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("A symbolic link replaced part of the scan path.");
            if (string.Equals(here, root, StringComparison.Ordinal)) break;
        }
    }
}
