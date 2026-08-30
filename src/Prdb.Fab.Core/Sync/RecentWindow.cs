namespace Prdb.Fab.Core.Sync;

/// <summary>
/// The fixed rolling interval for which Sync guarantees prepared Catalogue,
/// Indexer Cache and Identification data without user action.
/// </summary>
public static class RecentWindow
{
    public const int Days = 90;

    public static readonly TimeSpan Length = TimeSpan.FromDays(Days);

    /// <summary>
    /// Work becomes due before the public twenty-four-hour guarantee so a
    /// bounded pass has time to finish without drifting past it.
    /// </summary>
    public static readonly TimeSpan RevalidateAfter = TimeSpan.FromHours(23);

    public static readonly TimeSpan CompleteEvery = TimeSpan.FromHours(24);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(25);

    public static DateTimeOffset BeginsAt(DateTimeOffset now) => now - Length;

    public static bool Contains(DateTimeOffset publishedAt, DateTimeOffset now) =>
        publishedAt >= BeginsAt(now);

    public static TimeSpan NextPassIn(DateTimeOffset passStartedAt, DateTimeOffset now)
    {
        var remaining = CompleteEvery - (now - passStartedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
