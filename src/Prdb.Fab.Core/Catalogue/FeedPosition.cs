using System.Globalization;

namespace Prdb.Fab.Core.Catalogue;

/// <summary>
/// How far the sync has come with one feed, and the rule that reads it back
/// out: what to ask prdb for next.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0013's overlap, as a value. A cursor advanced to exactly the last value
/// seen loses every row sharing it, because prdb's <c>since</c> is a lower
/// bound over a timestamp several rows can carry — and a bulk import is
/// precisely the case where they do. So the stored position is set back by
/// <see cref="Overlap"/> before it is used, and every result is applied as an
/// idempotent upsert. That is one rule and this is the one place it lives; the
/// change feeds and What's New both read it here.
/// </para>
/// <para>
/// The tie-breaker is what keeps the overlap from being a trap. Setting a
/// position back a minute and asking again is safe only for as long as the tool
/// gets <em>past</em> that minute: a feed handing back a full page of rows
/// inside it would be replayed from the same place forever. So a position knows
/// whether the walk it came from was finished, and an unfinished one resumes
/// from the exact row it stopped at — no overlap, no ambiguity, and progress
/// guaranteed. The presence of the id is what says which of the two this is,
/// rather than a second field saying it again.
/// </para>
/// </remarks>
public sealed record FeedPosition
{
    /// <summary>
    /// How far back a settled position is set before it is used.
    /// </summary>
    /// <remarks>
    /// One minute, the number ADR 0013 left to the ticket that owns polling.
    /// It is a bound on how far prdb's own clock and its commit order may
    /// disagree, not a bound on how late a row may be — a row is found by its
    /// <c>updatedAtUtc</c>, whenever the tool gets round to asking.
    /// </remarks>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    private FeedPosition(DateTimeOffset at, Guid? unfinished)
    {
        At = at;
        Unfinished = unfinished;
    }

    /// <summary>The last point the tool knows it has seen everything up to.</summary>
    public DateTimeOffset At { get; }

    /// <summary>
    /// The row the walk stopped at, when it stopped in the middle of one. Null
    /// once the feed has said there is nothing more, which is the ordinary
    /// state and the one the overlap applies to.
    /// </summary>
    public Guid? Unfinished { get; }

    /// <summary>
    /// The feed said there is nothing more. The next request sets this back by
    /// <see cref="Overlap"/>.
    /// </summary>
    public static FeedPosition CaughtUpAt(DateTimeOffset at) => new(at, unfinished: null);

    /// <summary>
    /// The page ended with more behind it. The next request resumes from
    /// exactly here, because a walk that is still running has nothing to be
    /// conservative about and everything to lose by not advancing.
    /// </summary>
    public static FeedPosition MidWalkAt(DateTimeOffset at, Guid row) => new(at, row);

    /// <summary>What to send as <c>since</c>.</summary>
    public DateTimeOffset Since => Unfinished is null ? At - Overlap : At;

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
