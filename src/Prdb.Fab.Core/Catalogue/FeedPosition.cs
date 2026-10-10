using System.Globalization;

namespace Prdb.Fab.Core.Catalogue;

/// <summary>A durable server timestamp and optional row tie-breaker.</summary>
/// <remarks>
/// Change feeds resume exactly. The server supplies its two-minute settle bound;
/// adding a client overlap repeats pages and can stall progress. Discovery uses
/// <see cref="DiscoverySince"/> because CreatedAfter has no UUID tie-breaker.
/// Existing timestamp and timestamp|UUID strings remain readable.
/// </remarks>
public sealed record FeedPosition
{
    /// <summary>Discovery's overlap for exclusive CreatedAfter reads.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    private FeedPosition(DateTimeOffset at, Guid? unfinished)
    {
        At = at;
        Unfinished = unfinished;
    }

    /// <summary>The last point the tool knows it has seen everything up to.</summary>
    public DateTimeOffset At { get; }

    /// <summary>
    /// The last received row, including on a nonempty final page. Null after
    /// an empty page advances to the server bound.
    /// </summary>
    public Guid? Unfinished { get; }

    /// <summary>
    /// A server bound without a row tie-breaker.
    /// </summary>
    public static FeedPosition CaughtUpAt(DateTimeOffset at) => new(at, unfinished: null);

    /// <summary>
    /// The page ended with more behind it. The next request resumes from
    /// exactly here, because a walk that is still running has nothing to be
    /// conservative about and everything to lose by not advancing.
    /// </summary>
    public static FeedPosition MidWalkAt(DateTimeOffset at, Guid row) => new(at, row);

    /// <summary>
    /// Before everything, for a feed that has never run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// prdb requires <c>since</c> on every feed request and answers <c>400</c>
    /// without it, so <em>from the beginning</em> has to be a value rather than
    /// an absence. Its own OpenAPI document does not mark the parameter as
    /// required, which is why the first version of this code expressed the
    /// beginning by sending nothing at all and no test refused it: the document
    /// the fake was built from allowed what the service does not.
    /// </para>
    /// <para>
    /// The lower bound that excludes nothing. A timestamp rather than a
    /// hard-coded string, so the one rule about what to send lives in
    /// <see cref="Since"/> like every other.
    /// </para>
    /// </remarks>
    public static FeedPosition TheBeginning { get; } = new(DateTimeOffset.MinValue, unfinished: null);

    /// <summary>The exact change-feed timestamp.</summary>
    public DateTimeOffset Since => At;

    /// <summary>Discovery retains its separate exclusive-bound overlap.</summary>
    public DateTimeOffset DiscoverySince => At - DateTimeOffset.MinValue < Overlap ? At : At - Overlap;

    /// <summary>
    /// What to send as <c>sinceId</c>. Never set together with a timestamp that
    /// has been moved: an id tie-breaks rows at one timestamp, so pairing it
    /// with a different one would skip everything in the overlap that sorts
    /// below it — which is the failure the overlap exists to prevent.
    /// </summary>
    public Guid? SinceId => Unfinished;

    /// <summary>
    /// The one string the <c>FeedCursor</c> row holds. Round-trip format
    /// throughout, so what is on disk is readable by whoever opens the database
    /// to ask why a feed is where it is.
    /// </summary>
    public string Stored => Unfinished is { } row
        ? $"{Timestamp(At)}|{row:D}"
        : Timestamp(At);

    /// <summary>
    /// A stored position, or null where there is none to read.
    /// </summary>
    /// <remarks>
    /// Unparseable text answers the same as no text at all. A cursor is prdb's
    /// token in one direction and this tool's own writing in the other, and
    /// neither is worth failing a routine over: starting the feed again from
    /// the beginning costs requests, and refusing to run costs the feed.
    /// </remarks>
    public static FeedPosition? Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var separator = stored.IndexOf('|', StringComparison.Ordinal);
        var when = separator < 0 ? stored : stored[..separator];

        if (!DateTimeOffset.TryParse(
                when,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var at))
        {
            return null;
        }

        if (separator < 0)
        {
            return CaughtUpAt(at);
        }

        return Guid.TryParse(stored[(separator + 1)..], out var row)
            ? MidWalkAt(at, row)
            : CaughtUpAt(at);
    }

    private static string Timestamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
