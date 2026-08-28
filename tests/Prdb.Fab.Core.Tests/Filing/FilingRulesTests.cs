using System.Xml.Linq;

using Prdb.Fab.Core.Filing;
using Prdb.Fab.Core.ReleaseDiscovery;

using Xunit;

namespace Prdb.Fab.Core.Tests.Filing;

public sealed class FilingRulesTests
{
    [Theory]
    [InlineData(3840, 2160, "2160p")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(1024, 576, "576p")]
    [InlineData(854, 480, "480p")]
    [InlineData(640, 360, "360p")]
    [InlineData(426, 240, "240p")]
    [InlineData(320, 180, "180p")]
    public void Quality_uses_the_fixed_ladder_and_keeps_a_truthful_fallback(
        int width,
        int height,
        string expected) =>
        Assert.Equal(expected, VideoQuality.LabelFor(width, height));

    [Fact]
    public void A_path_mapping_is_boundary_aware_case_aware_and_rejects_traversal()
    {
        var local = Path.Combine(Path.GetTempPath(), "mapped-root");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(local, "Movie", "video.mkv")),
            PathMapping.Resolve("C:\\Complete", local, "c:\\complete\\Movie\\video.mkv"));
        Assert.Null(PathMapping.Resolve("/complete", local, "/complete-other/video.mkv"));
        Assert.Null(PathMapping.Resolve("/complete", local, "/complete/../secret.mkv"));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(local, "complete", "video.mkv")),
            PathMapping.Resolve("/", local, "/complete/video.mkv"));
    }

    [Fact]
    public void The_after_download_gate_is_named_membership_not_enum_order()
    {
        var video = Guid.NewGuid();
        var exact = AfterDownloadGate.Admissions(AfterDownloadGateChoice.ExactOnly);
        var ordinary = AfterDownloadGate.Admissions(AfterDownloadGateChoice.ExactAndStrong);

        Assert.True(AfterDownloadGate.Admits(video, IdentificationConfidence.Exact, exact));
        Assert.False(AfterDownloadGate.Admits(video, IdentificationConfidence.Strong, exact));
        Assert.True(AfterDownloadGate.Admits(video, IdentificationConfidence.Strong, ordinary));
        Assert.False(AfterDownloadGate.Admits(video, IdentificationConfidence.Ambiguous, ordinary));
        Assert.False(AfterDownloadGate.Admits(null, IdentificationConfidence.Exact, ordinary));
    }

    [Fact]
    public void Only_a_known_different_filesystem_chooses_copy_verify_delete()
    {
        Assert.Equal(FilingMove.Rename, FilingMoves.For(true));
        Assert.Equal(FilingMove.Rename, FilingMoves.For(null));
        Assert.Equal(FilingMove.CopyVerifyDelete, FilingMoves.For(false));
    }

    [Fact]
    public void The_sidecar_writes_only_the_five_Jellyfin_shapes_and_escapes_text()
    {
        var video = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000101");
        var sidecar = XDocument.Parse(Sidecar.For(new SidecarMetadata(
            video,
            "A & B < C",
            "A Site",
            new DateOnly(2026, 8, 28),
            ["An Actor", ""])));

        Assert.Equal("A & B < C", sidecar.Root!.Element("title")!.Value);
        Assert.Equal("2026-08-28", sidecar.Root.Element("premiered")!.Value);
        Assert.Equal("A Site", sidecar.Root.Element("studio")!.Value);
        var actor = Assert.Single(sidecar.Root.Elements("actor"));
        Assert.Equal("An Actor", actor.Element("name")!.Value);
        Assert.Equal("Actor", actor.Element("type")!.Value);
        Assert.Equal(video.ToString("D"), sidecar.Root.Element("uniqueid")!.Value);
        Assert.Equal("prdb", sidecar.Root.Element("uniqueid")!.Attribute("type")!.Value);
        Assert.Equal(5, sidecar.Root.Elements().Count());
    }
}
