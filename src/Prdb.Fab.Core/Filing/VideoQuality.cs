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
}
