using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// What is pinned, as ADR 0033 fixed it: a query, and not a column.
/// </summary>
/// <remarks>
/// <para>
/// A stored flag would have six writers and no reader that would notice a
/// mistake. A pin never cleared keeps a row forever and shows only as a cache
/// that quietly stops evicting; a pin cleared too early drops exactly the row
/// ADR 0015 said must never be dropped. Neither failure has a symptom, which is
/// why the answer is computed from what points at the row every time it is
/// asked.
/// </para>
/// <para>
/// The performance objection that made ADR 0013 assume a column does not
/// survive, and the reason is <see cref="CatalogueEviction"/> rather than
/// anything here: eviction walks candidates in first-seen order and stops, so
/// the clauses below are evaluated over the rows it looks at rather than over
/// the table. This class exists to be composed into somebody else's query, and
/// every method on it takes the rows the caller is already narrowing.
/// </para>
/// <para>
/// Diagnosis comes free. The clauses are named, so <see cref="WhyAsync"/> can
/// answer <em>why is this row pinned</em> without a column that can be wrong.
/// Nothing displays it yet; the answer exists.
/// </para>
/// </remarks>
public sealed class CataloguePins(FabDbContext context, IEnumerable<ICataloguePin> sources)
{
    private readonly IReadOnlyList<ICataloguePin> sources = [.. sources];

    /// <summary>Which sources are asked. In practice the ones that are registered.</summary>
    public IReadOnlyList<PinReason> Reasons => [.. sources.Select(source => source.Reason)];

    /// <summary>The rows of <paramref name="videos"/> something points at.</summary>
    public IQueryable<CatalogueVideoRow> Pinned(IQueryable<CatalogueVideoRow> videos) =>
        AnyOf() is { } pinned ? videos.Where(pinned) : videos.Where(_ => false);

    /// <summary>
    /// The rows of <paramref name="videos"/> nothing points at, which is what
    /// eviction may take.
    /// </summary>
    /// <remarks>
    /// Written as a chain of negated clauses rather than as the negation of the
    /// chain. The two are the same answer, and this one keeps each source's
    /// <c>NOT EXISTS</c> a clause of its own — which is what SQLite is asked to
    /// short-circuit against the indexes the sources bring.
    /// </remarks>
    public IQueryable<CatalogueVideoRow> Unpinned(IQueryable<CatalogueVideoRow> videos) =>
        sources.Aggregate(videos, (narrowed, source) => narrowed.Where(Not(source.PointsAt)));

    /// <summary>Whether anything points at the video with this local id.</summary>
    public async Task<bool> IsPinnedAsync(long videoId, CancellationToken cancellationToken) =>
        await Pinned(context.CatalogueVideos.Where(row => row.Id == videoId))
            .AnyAsync(cancellationToken);

    /// <summary>
    /// Which sources point at the video with this local id, in the order they
    /// are asked. Empty for a row nothing points at.
    /// </summary>
    /// <remarks>
    /// One query per source rather than one query with a column per source: the
    /// question is asked about a single row by somebody who wants the answer
    /// written out, which is a different job from the one eviction does in bulk.
    /// Nothing calls it yet — ADR 0018's page is where a reason of this kind
    /// would be shown — and it is here because the clauses being named is what
    /// makes it possible at all.
    /// </remarks>
    public async Task<IReadOnlyList<PinReason>> WhyAsync(long videoId, CancellationToken cancellationToken)
    {
        var reasons = new List<PinReason>();

        foreach (var source in sources)
        {
            var points = await context.CatalogueVideos
                .Where(row => row.Id == videoId)
                .AnyAsync(source.PointsAt, cancellationToken);

            if (points)
            {
                reasons.Add(source.Reason);
            }
        }

        return reasons;
    }

    /// <summary>
    /// Every source's clause, joined with <c>OR</c>, or null where there are no
    /// sources at all — which is not the same predicate as <em>false</em> to
    /// whoever reads it.
    /// </summary>
    private Expression<Func<CatalogueVideoRow, bool>>? AnyOf() =>
        sources.Count == 0
            ? null
            : sources.Select(source => source.PointsAt).Aggregate(Or);

    private static Expression<Func<CatalogueVideoRow, bool>> Not(
        Expression<Func<CatalogueVideoRow, bool>> clause) =>
        Expression.Lambda<Func<CatalogueVideoRow, bool>>(
            Expression.Not(clause.Body),
            clause.Parameters);

    /// <summary>
    /// Two clauses as one. The right-hand side is rewritten onto the left's
    /// parameter, because two lambdas written separately name the same video
    /// with two different parameters and an expression tree does not know they
    /// mean the same thing.
    /// </summary>
    private static Expression<Func<CatalogueVideoRow, bool>> Or(
        Expression<Func<CatalogueVideoRow, bool>> left,
        Expression<Func<CatalogueVideoRow, bool>> right)
    {
        var video = left.Parameters[0];

        var body = Expression.OrElse(
            left.Body,
            new Rebind(right.Parameters[0], video).Visit(right.Body));

        return Expression.Lambda<Func<CatalogueVideoRow, bool>>(body, video);
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : node;
    }
}
