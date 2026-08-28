using System.Linq.Expressions;

using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.ReleaseDiscovery;

/// <summary>Composes every current pin source into the eviction query.</summary>
public sealed class ReleasePins(IEnumerable<IReleasePin> sources)
{
    private readonly IReadOnlyList<IReleasePin> sources = [.. sources];

    public IQueryable<ReleaseRow> Unpinned(IQueryable<ReleaseRow> releases) =>
        sources.Aggregate(releases, (query, source) => query.Where(Not(source.PointsAt)));

    private static Expression<Func<ReleaseRow, bool>> Not(Expression<Func<ReleaseRow, bool>> clause) =>
        Expression.Lambda<Func<ReleaseRow, bool>>(Expression.Not(clause.Body), clause.Parameters);
}
