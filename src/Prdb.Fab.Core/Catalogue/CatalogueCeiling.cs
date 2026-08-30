namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// The ordinary catalogue row ceiling, below the stronger Recent Window and
/// pin obligations.
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
/// Fifty thousand videos is the disposable-cache ceiling. ADR 0050 permits the
/// table to exceed it when ninety days of current source volume or pins require
/// more; eviction resumes as rows leave those protected sets.
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
