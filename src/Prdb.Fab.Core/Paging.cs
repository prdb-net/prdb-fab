namespace Prdb.Fab.Core;

/// <summary>
/// The two pieces of arithmetic every paged surface does, in one place.
/// </summary>
/// <remarks>
/// <para>
/// In no area's namespace, because it belongs to no area: the catalogue grids,
/// the release table, the download list, the review queue, the library and the
/// operation log all page the same way, and each of them had its own copy of
/// this arithmetic before.
/// </para>
/// <para>
/// A page number is counted from one because ADR 0036 puts it in the address
/// bar and a person reads it there — which also means a person can type
/// anything into it. Flooring at one was already done at every call site; what
/// was not is the ceiling, and <c>(page - 1) * pageSize</c> overflows to a
/// negative offset well before <c>int.MaxValue</c>.
/// </para>
/// <para>
/// A negative offset does not throw: it is treated as zero, so the surface
/// answers with the <em>first</em> page while reporting the number that was
/// asked for. Being shown page one and told it is page two billion is a worse
/// answer than an empty page, which is what a number past the end should give.
/// </para>
/// </remarks>
public static class Paging
{
    /// <summary>The page being asked for, as a number a page can have.</summary>
    public static int Wanted(int page) => Math.Max(page, 1);

    /// <summary>
    /// How many rows to skip to reach <paramref name="page"/>, never negative
    /// and never past what an <see cref="int"/> holds.
    /// </summary>
    public static int Skip(int page, int pageSize)
    {
        var offset = (long)(Wanted(page) - 1) * Math.Max(pageSize, 0);

        return offset > int.MaxValue ? int.MaxValue : (int)offset;
    }
}
