namespace Prdb.Fab.Core.Sync;

/// <summary>
/// Every number ADR 0061 fixes about prdb's user previews, in the one place the
/// code reads them from.
/// </summary>
/// <remarks>
/// <para>
/// A contract written twice is a contract that will disagree with itself, so the
/// ADR's table and this class are the same figures and the ADR is the argument
/// for each of them. What is here is what more than one slice needs: the
/// synchronisation, the asset cache, the gallery, the Identification evidence
/// and the Library all read from it.
/// </para>
/// <para>
/// None of it is a setting. ADR 0020 admits a control where the answer lives
/// outside anything the tool can observe, and every figure below is either a
/// judgement about this tool or a bound on somebody else's payload.
/// </para>
/// </remarks>
public static class UserPreviewContract
{
    /// <summary>
    /// How long a Video's user previews are taken at their word before they are
    /// asked about again — including when the answer was <em>none</em>.
    /// </summary>
    /// <remarks>
    /// A week, the same figure and the same argument as ADR 0060's
    /// <c>Recently</c>: a Video with no user preview is the ordinary case and
    /// must not cost a request every time somebody glances at it, and a Video
    /// that acquires one is normally brought by the change feed rather than by
    /// a person noticing. This is the backstop for the row the feed's cursor
    /// was already past.
    /// </remarks>
    public static readonly TimeSpan Freshness = TimeSpan.FromDays(7);

    /// <summary>
    /// How long an interest in a Video's user previews survives nothing
    /// touching it.
    /// </summary>
    /// <remarks>
    /// Ninety days, which is <c>RecentWindow</c>'s span and is chosen to agree
    /// with it: an interest older than the window over a Video nothing pins is
    /// a Video this installation has stopped having an opinion about. Expiring
    /// it takes its preview rows and their bytes with it, which is what keeps
    /// the population proportional to what is looked at.
    /// </remarks>
    public static readonly TimeSpan InterestExpiry = TimeSpan.FromDays(90);

    /// <summary>
    /// How often the change feed is looked at while anything is interested.
    /// </summary>
    /// <remarks>
    /// An hour. What it carries is moderation catching up with a population
    /// this installation holds a fraction of a percent of, and the cost of
    /// being an hour behind it is a withdrawn picture shown for up to an hour
    /// longer. The feed is a routine with a work set (ADR 0032), so an
    /// installation nobody has browsed spends nothing on it at all — which is
    /// why <see cref="IdleProfile.RequestsAnHour"/> does not move.
    /// </remarks>
    public static readonly TimeSpan FeedCadence = TimeSpan.FromHours(1);

    /// <summary>
    /// The most user previews kept for one Video.
    /// </summary>
    /// <remarks>
    /// Sixty, taken in prdb's own order — <c>displayOrder</c>, then
    /// <c>createdAtUtc</c>, then <c>id</c> — and it is a stop rather than a
    /// budget. A Video with sixty user previews is one whose gallery nobody
    /// reaches the end of; what the bound refuses is a Video that has acquired
    /// thousands, which would make this table a multiple of the one it
    /// describes for a surface that shows a strip.
    /// </remarks>
    public const int RowsPerVideo = 60;

    /// <summary>
    /// The most one sprite sheet or single image may weigh before it is
    /// refused.
    /// </summary>
    /// <remarks>
    /// Eight megabytes. Half of <c>ArtworkCeiling.AnImage</c> and for the
    /// opposite reason: that one is drawn wide because prdb's own images are
    /// small and the ceiling is only there to catch an answer that is not an
    /// image at all. A sprite sheet is genuinely large — a hundred tiles of
    /// 320x180 is comfortably under a megabyte, four hundred at 480x270 is a
    /// few — so this one is a real bound on real content, and it is set where
    /// a legitimate sheet does not reach it.
    /// </remarks>
    public const long ASprite = 8L * 1024 * 1024;

    /// <summary>The most one WebVTT may weigh.</summary>
    /// <remarks>
    /// A quarter of a megabyte, which is around fifty bytes a cue at the cue
    /// ceiling with room to spare for comments and a byte-order mark. It is
    /// read into memory whole to be parsed, which is why it has a bound of its
    /// own rather than sharing the sprite's.
    /// </remarks>
    public const long AVtt = 256L * 1024;

    /// <summary>
    /// The most cues one WebVTT may carry, and the most tiles one sprite sheet
    /// may be cut into.
    /// </summary>
    /// <remarks>
    /// Four hundred, one figure for both because a sound pair has one cue per
    /// tile. Four hundred tiles over a scene of any ordinary length is a
    /// picture every few seconds, which is finer than a scrubbing strip is read
    /// at; beyond it the sheet stops being a preview and starts being a
    /// filmstrip nothing can decode inside a page load.
    /// </remarks>
    public const int Tiles = 400;

    /// <summary>
    /// The most disk the user-preview asset cache may hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two gigabytes, its own ceiling rather than a share of ADR 0030's eight.
    /// The two populations are bounded for different reasons and would
    /// otherwise compete: the artwork cache is sized so that a browse grid does
    /// not go to a CDN, which is a figure ADR 0059 measured against the whole
    /// Catalogue, and this one is sized for the Videos somebody has actually
    /// opened or filed. A shared ceiling would let a week of browsing sprite
    /// sheets evict the warm grid ADR 0059 exists to keep.
    /// </para>
    /// <para>
    /// It counts both halves of a pair. A WebVTT is a rounding error beside its
    /// sprite, and leaving it out would mean the figure on the status page is
    /// not the figure on the disk.
    /// </para>
    /// </remarks>
    public const long CacheBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// How many bytes have to go for a cache of <paramref name="held"/> to be
    /// back under <paramref name="ceiling"/>, and zero where none do.
    /// </summary>
    public static long OverBy(long held, long ceiling = CacheBytes) =>
        held > ceiling ? held - ceiling : 0;
}
