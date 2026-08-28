using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Core.Filing;

/// <summary>ADR 0006's fixed named sets for acting on an Arriving File.</summary>
public static class AfterDownloadGate
{
    public const string Name = "AfterDownload";

    public static IReadOnlySet<IdentificationConfidence> Admissions(AfterDownloadGateChoice choice) =>
        choice switch
        {
            AfterDownloadGateChoice.ExactOnly => ExactOnly,
            AfterDownloadGateChoice.ExactAndStrong => ExactAndStrong,
            _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null),
        };

    public static bool Admits(
        Guid? videoId,
        IdentificationConfidence? confidence,
        IReadOnlySet<IdentificationConfidence> admissions) =>
        videoId is not null && confidence is { } named && admissions.Contains(named);

    private static readonly HashSet<IdentificationConfidence> ExactOnly =
        [IdentificationConfidence.Exact];

    private static readonly HashSet<IdentificationConfidence> ExactAndStrong =
        [IdentificationConfidence.Exact, IdentificationConfidence.Strong];
}

public enum AfterDownloadGateChoice
{
    ExactOnly,
    ExactAndStrong,
}
