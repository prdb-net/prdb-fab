using System.Globalization;

namespace Prdb.Fab.Core.Reporting;

/// <summary>The three quality rungs prdb can receive in a Fulfilment.</summary>
public enum FulfilmentQuality
{
    P720,
    P1080,
    P2160,
}

/// <summary>
/// ADR 0019's deliberately conservative conversion from a filed quality label
/// to prdb's coarser scale.
/// </summary>
public static class FulfilmentQualities
{
    public static FulfilmentQuality? HighestTruthfullyReportable(IEnumerable<string> qualityLabels)
    {
        var highest = qualityLabels
            .Select(HeightOf)
            .DefaultIfEmpty(0)
            .Max();

        return highest switch
        {
            >= 2160 => FulfilmentQuality.P2160,
            >= 1080 => FulfilmentQuality.P1080,
            >= 720 => FulfilmentQuality.P720,
            _ => null,
        };
    }

    private static int HeightOf(string label) =>
        label.EndsWith('p')
        && int.TryParse(label.AsSpan(0, label.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            ? height
            : 0;
}
