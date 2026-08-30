using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Prdb.Fab.Core.Catalogue;
using Prdb.Fab.Core.Sync;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// Holding the catalogue to <see cref="CatalogueCeiling"/> by dropping the
/// oldest rows nothing points at, outside the protected Recent Window.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It walks candidates; it does not scan.</strong> That sentence is
/// what ADR 0033 rested on when it corrected ADR 0013 and made pinning a query:
/// the performance objection to a query assumed a table-wide anti-join, and
/// this is the implementation the argument assumed instead. Rows are taken in
/// first-seen order, the clauses of <see cref="CataloguePins"/> are evaluated
/// over the ones it looks at, and it stops as soon as enough has been freed.
/// </para>
/// <para>
/// One window per pass, which is the same shape <c>ChangeFeedRoutine</c> has
/// and for the same reason: a bounded run yields, and being behind is answered
/// by coming round again rather than by not stopping. So the number of rows
/// read is <see cref="AWindow"/> whatever the catalogue has grown to, and a
/// catalogue far over its ceiling comes down over several passes rather than in
/// one that holds the lane.
/// </para>
/// <para>
/// First-seen order is the ascending surrogate id. ADR 0033 spends no timestamp
/// on when a catalogue row was created — the row carries prdb's own stamp and
/// the tool's <em>last re-read</em>, neither of which is this — and an integer
/// key handed out in order already says which row arrived first.
/// </para>
/// <para>
/// There is no routine here. ADR 0030 puts eviction in the same bulk-lane
/// routine as the artwork work set, so the routine arrives with the artwork
/// cache and calls this.
/// </para>
/// </remarks>
public sealed class CatalogueEviction(
    FabDbContext context,
    CataloguePins pins,
    TimeProvider time,
    ILogger<CatalogueEviction> logger)
{
    /// <summary>
    /// How many rows one pass looks at, oldest first.
    /// </summary>
    /// <remarks>
    /// Five hundred, which is what a pass costs at its most expensive: one
    /// count, one anti-join over five hundred rows, one delete. The ceiling is
    /// a hundred times that, so a catalogue arriving far over it is brought
    /// down by repeated passes — and the routine that turns this is due again
    /// as soon as the work set is not empty (ADR 0032).
    /// </remarks>
    public const int AWindow = 500;

    /// <summary>
    /// Takes the catalogue back towards <paramref name="ceiling"/>, as far as
    /// one window reaches.
    /// </summary>
    /// <remarks>
    /// Deleting the video row takes its pre-names, its credits and its image
    /// rows with it: ADR 0033 declares those cascades and SQLite is opened with
    /// <c>foreign_keys=ON</c> (ADR 0039), so the database is what enforces it
    /// rather than a list here that could fall behind the schema. The cached
    /// image <em>files</em> are ADR 0030's to sweep, in the routine that turns
    /// this.
    /// </remarks>
    public async Task<Eviction> EvictAsync(
        int ceiling = CatalogueCeiling.Rows,
        CancellationToken cancellationToken = default)
    {
        var held = await context.CatalogueVideos.CountAsync(cancellationToken);
        var over = CatalogueCeiling.OverBy(held, ceiling);

        if (over == 0)
        {
            return new Eviction(held, Removed: 0, Examined: 0);
        }

        // The old rows this pass looks at, and the only rows it looks at. The count
        // is not measured afterwards: it is the LIMIT in the query below, so
        // what is reported is what was asked for.
        var examined = Math.Min(AWindow, held);

        var recentSince = RecentWindow.BeginsAt(time.GetUtcNow());
        var window = context.CatalogueVideos
            .Where(row => row.CreatedAtUtc < recentSince)
            .OrderBy(row => row.Id)
            .Take(AWindow);

        var evictable = await pins.Unpinned(window)
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .Take(over)
            .ToListAsync(cancellationToken);

        if (evictable.Count == 0)
        {
            // Everything available to this pass is recent or pinned. Not a
            // failure: the Recent Window and the rows something local points
            // at are both stronger obligations than the count ceiling.
            logger.LogWarning(
                "The catalogue holds {Held} video(s), {Over} over its ceiling of {Ceiling}, and the "
                + "oldest {Examined} candidates are protected.",
                held,
                over,
                ceiling,
                examined);

            return new Eviction(held, Removed: 0, examined);
        }

        var removed = await context.CatalogueVideos
            .Where(row => evictable.Contains(row.Id))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation(
            "Evicted {Removed} catalogue video(s) of the oldest {Examined}, leaving {Left} against a "
            + "ceiling of {Ceiling}.",
            removed,
            examined,
            held - removed,
            ceiling);

        return new Eviction(held, removed, examined);
    }
}

/// <summary>What one pass of eviction did.</summary>
/// <param name="Held">How many catalogue videos there were before it ran.</param>
/// <param name="Removed">How many it dropped.</param>
/// <param name="Examined">
/// How many rows it looked at. Bounded by <see cref="CatalogueEviction.AWindow"/>
/// whatever <paramref name="Held"/> is, which is the whole of ADR 0033's answer
/// to the objection that a query cannot replace a column.
/// </param>
public sealed record Eviction(int Held, int Removed, int Examined);
