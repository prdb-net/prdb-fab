using System.Globalization;

namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// How far back the first pass over prdb's videos goes, and how far it has come.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0013 bounds the backfill by a <strong>page count</strong> and not by a
/// date window, for the same reason it refused a time-based eviction rule: a
/// window of days has an unpredictable cost, because nobody knows how many
/// videos prdb adds per day and the API document states no row count anywhere.
/// A window of pages has a stated one, and it is the number below.
/// </para>
/// <para>
/// Twenty pages of a hundred, which is what the ticket that owns polling
/// proposed. The cost is not twenty requests: a page of a hundred ids costs one
/// request to discover and two more to read back at
/// <c>POST /videos/batch</c>'s fifty a request, because ADR 0013 has no
/// catalogue row arriving without a detail read. Sixty requests, once, in the
/// bulk lane, behind everything else the governor is holding back.
/// </para>
/// </remarks>
public static class Backfill
{
    /// <summary>The last page the backfill reads before it retires.</summary>
    public const int LastPage = 20;

    /// <summary><c>GET /videos</c>'s largest page.</summary>
    public const int APage = 100;

    /// <summary><c>POST /videos/batch</c>'s limit, and what a page costs to read back.</summary>
    public const int ABatch = 50;

    /// <summary>
    /// The page a stored position names, or the first where there is nothing
    /// readable to resume from.
    /// </summary>
    public static int PageIn(string? stored) =>
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page >= 1
            ? page
            : 1;

    /// <summary>What a position looks like on disk.</summary>
    public static string Stored(int page) => page.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="page"/> is past the ceiling.</summary>
    public static bool Beyond(int page) => page > LastPage;
}
