using Prdb.Fab.Core.ReleaseDiscovery;

namespace Prdb.Fab.Core.Automation;

/// <summary>ADR 0006's fixed named sets for acting on an identified Release.</summary>
public static class BeforeDownloadGate
{
    public const string Name = "BeforeDownload";

    public static IReadOnlySet<IdentificationConfidence> Admissions(BeforeDownloadGateChoice choice) =>
        choice switch
        {
            BeforeDownloadGateChoice.ExactOnly => ExactOnly,
            BeforeDownloadGateChoice.ExactAndStrong => ExactAndStrong,
            BeforeDownloadGateChoice.ThroughProbable => ThroughProbable,
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

    private static readonly HashSet<IdentificationConfidence> ThroughProbable =
        [IdentificationConfidence.Exact, IdentificationConfidence.Strong, IdentificationConfidence.Probable];
}

public enum BeforeDownloadGateChoice
{
    ExactOnly,
    ExactAndStrong,
    ThroughProbable,
}
