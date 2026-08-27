using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Skeleton;

/// <summary>What the one route reads and writes. Scaffolding; see the row type.</summary>
public sealed record SkeletonItem(long Id, string Label, DateTimeOffset AddedAt, DateTimeOffset? SweptAt);

/// <summary>
/// Adding an item and listing them. Scaffolding, and the smallest thing that
/// makes the skeleton walk: the route gives the routine work, and the routine
/// gives the route something that changes.
/// </summary>
public sealed class SkeletonItems(FabDbContext context, TimeProvider time)
{
    public async Task<IReadOnlyList<SkeletonItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await context.SkeletonItems
            .OrderByDescending(row => row.Id)
            .Select(row => new SkeletonItem(row.Id, row.Label, row.AddedAt, row.SweptAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<SkeletonItem> AddAsync(string label, CancellationToken cancellationToken)
    {
        var row = new SkeletonItemRow
        {
            Label = label,

            // ADR 0042: nothing reads the clock directly, and an architecture
            // test fails the build over it. The sibling project injects
            // TimeProvider everywhere and still calls DateTimeOffset.UtcNow
            // once, which is why the rule is enforced rather than agreed.
            AddedAt = time.GetUtcNow(),
        };

        context.SkeletonItems.Add(row);
        await context.SaveChangesAsync(cancellationToken);

        return new SkeletonItem(row.Id, row.Label, row.AddedAt, row.SweptAt);
    }
}
