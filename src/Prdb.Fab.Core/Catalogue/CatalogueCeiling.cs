namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// How large the catalogue is allowed to get, as the row count ADR 0013 chose
/// over a duration.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0013 rejected <em>drop what has not been looked at for thirty days</em>
/// for exactly one reason: nobody can predict how much disk a duration implies,
/// because nobody knows how many videos prdb adds per day and the API document
/// states no row count anywhere. A maximum row count is a number that can be
/// written in the documentation and held in the head, and this is that number.
/// </para>
/// <para>
/// Fifty thousand videos. The arithmetic behind it: ADR 0013's backfill brings
/// two thousand rows once (<see cref="Backfill.LastPage"/> pages of
/// <see cref="Backfill.APage"/>), and What's New adds whatever prdb publishes
/// from then on — so the ceiling is what says how much of that history a cache
/// keeps rather than how much of it exists. At a few hundred bytes a row, with
/// its pre-names, its credits and its image rows beside it, fifty thousand is
/// tens of megabytes on a volume ADR 0034 already sizes in gigabytes, and it is
/// far more than the pinned part of any library this tool is meant for.
/// </para>
/// <para>
/// Not a setting. ADR 0020 admits a control only where the answer lives outside
/// anything the tool can observe, and this is a number about the tool's own
/// disk — see <c>docs/running-in-docker.md</c>, where what grows on the data
/// volume is written down.
/// </para>
/// </remarks>
public static class CatalogueCeiling
{
    /// <summary>The most catalogue videos the tool holds.</summary>
    public const int Rows = 50_000;

    /// <summary>
    /// How many rows have to go for a catalogue of <paramref name="held"/> to be
    /// back under <paramref name="ceiling"/>, and zero where none do.
    /// </summary>
    public static int OverBy(int held, int ceiling = Rows) => held > ceiling ? held - ceiling : 0;
}
