using Microsoft.EntityFrameworkCore;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

internal static class IndexerTargets
{
    public static async Task<IReadOnlyList<string>> CanonicalAsync(
        IQueryable<Guid> query,
        CancellationToken cancellationToken)
    {
        // SQLite renders GUID text in upper case when ToString is translated
        // into SQL, while .NET renders it in lower case. Materialise the GUIDs
        // first so every durable routine target uses one canonical spelling.
        var ids = await query.ToListAsync(cancellationToken);
        return ids.Select(id => id.ToString("D")).ToList();
    }
}
