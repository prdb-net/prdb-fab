using System.Globalization;

namespace Prdb.Fab.Core.Filing;

/// <summary>ADR 0011's fixed quality ladder, read from both picture dimensions.</summary>
public static class VideoQuality
{
    private static readonly (int Height, int Width, string Label)[] Standards =
    [
        (2160, 3840, "2160p"),
        (1440, 2560, "1440p"),
        (1080, 1920, "1080p"),
        (720, 1280, "720p"),
        (576, 1024, "576p"),
        (480, 854, "480p"),
        (360, 640, "360p"),
        (240, 426, "240p"),
    ];

    /// <summary>
    /// The ladder read as an order, best rung first. Every list of quality
    /// labels a person is shown is sorted with this, because sorting labels as
    /// text reads <c>1080p</c> as better than <c>720p</c> and buries
    /// <c>2160p</c> in the middle. A label the ladder does not name — the
    /// fallback below its last rung — sorts after every rung it does name.
    /// </summary>
    public static readonly IComparer<string> BestFirst = new BestFirstOrder();

    public static string LabelFor(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        for (var index = 0; index < Standards.Length; index++)
        {
            var standard = Standards[index];
            var next = index + 1 < Standards.Length
                ? Standards[index + 1]
                : (Height: 0, Width: 0, Label: string.Empty);

            if (height >= Halfway(standard.Height, next.Height)
                || width >= Halfway(standard.Width, next.Width))
            {
                return standard.Label;
            }
        }

        return height.ToString(CultureInfo.InvariantCulture) + "p";
    }

    private static int Halfway(int standard, int next) =>
        next == 0 ? standard : (standard + next) / 2;

    /// <summary>Where a label sits on the ladder. Higher is better, and a
    /// label the ladder does not name is zero.</summary>
    private static int RankOf(string? label)
    {
        for (var index = 0; index < Standards.Length; index++)
        {
            if (Standards[index].Label == label) return Standards.Length - index;
        }

        return 0;
    }

    private sealed class BestFirstOrder : IComparer<string>
    {
        public int Compare(string? left, string? right)
        {
            var byRung = RankOf(right).CompareTo(RankOf(left));

            // Two labels off the ladder are ordered by their text, so that a
            // page of them does not reshuffle between two requests.
            return byRung != 0 ? byRung : string.CompareOrdinal(left, right);
        }
    }
}
