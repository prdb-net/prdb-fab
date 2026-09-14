using System.Text;

using Prdb.Fab.Core.Sync;

using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

/// <summary>
/// ADR 0064's plan, and the one property worth the most: that the WebVTT it
/// writes says a tile is at the second the frame was actually taken from.
/// </summary>
/// <remarks>
/// The check is a round trip through <see cref="SpriteTimeline"/> — the same
/// parser that reads somebody else's sheet — because a strip whose cue times
/// are wrong is wrong in the one way nothing on screen would show: the picture
/// is fine, the scrub lands on the wrong moment, and no error is raised
/// anywhere.
/// </remarks>
public sealed class SpriteSheetPlanTests
{
    /// <summary>
    /// Every cue the plan writes covers the stretch its frame was taken from
    /// the middle of, in order, inside the sheet.
    /// </summary>
    [Theory]
    // The floor: a three-minute scene.
    [InlineData(180)]
    // The interval: twenty minutes at ten seconds a tile.
    [InlineData(1200)]
    // The ceiling: two hours.
    [InlineData(7200)]
    // An awkward number that divides into nothing.
    [InlineData(1337)]
    // A minute, which is the shortest file worth a strip at all.
    [InlineData(60)]
    public void Every_cue_holds_the_frame_it_names(int seconds)
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(seconds));
        var timeline = SpriteTimeline.Read(
            plan.Vtt(PreviewPublicationContract.SheetFilename),
            (plan.Width, plan.Height),
            PreviewPublicationContract.MostTiles);

        Assert.True(timeline.Usable, timeline.Reason);
        Assert.Equal(plan.Tiles, timeline.Tiles.Count);

        for (var index = 0; index < plan.Tiles; index++)
        {
            var tile = timeline.Tiles[index];
            var frame = plan.FrameAt(index);

            // The frame this tile shows was taken from between the cue's two
            // times — a millisecond either side, because the stamps are written
            // to the millisecond and the frame positions are not.
            Assert.InRange(frame, tile.Start - TimeSpan.FromMilliseconds(1), tile.End);

            var (x, y) = plan.TileOrigin(index);

            Assert.Equal((x, y), (tile.X, tile.Y));
            Assert.Equal(PreviewPublicationContract.TileWidth, tile.Width);
            Assert.Equal(PreviewPublicationContract.TileHeight, tile.Height);
        }
    }

    /// <summary>
    /// The last cue ends at the Runtime rather than a rounding error short of
    /// it, which is what makes a scrub to the end of the strip land on the last
    /// tile instead of on nothing.
    /// </summary>
    [Fact]
    public void The_strip_covers_the_whole_runtime()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(1337));
        var timeline = SpriteTimeline.Read(
            plan.Vtt(PreviewPublicationContract.SheetFilename),
            (plan.Width, plan.Height),
            PreviewPublicationContract.MostTiles);

        Assert.True(timeline.Usable, timeline.Reason);
        Assert.Equal(TimeSpan.Zero, timeline.Tiles[0].Start);
        Assert.InRange(
            timeline.Tiles[^1].End,
            plan.Runtime - TimeSpan.FromMilliseconds(1),
            plan.Runtime);

        // And no cue is a gap: each begins where the one before it ended.
        for (var index = 1; index < timeline.Tiles.Count; index++)
        {
            Assert.Equal(timeline.Tiles[index - 1].End, timeline.Tiles[index].Start);
        }
    }

    /// <summary>
    /// A four-hundred-tile strip's WebVTT fits inside the contract's ceiling
    /// with room to spare, so the largest legal sheet is not one that has to be
    /// refused after being made.
    /// </summary>
    [Fact]
    public void The_largest_strip_fits_inside_the_webvtt_ceiling()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromHours(10));

        Assert.Equal(PreviewPublicationContract.MostTiles, plan.Tiles);
        Assert.True(
            plan.Vtt(PreviewPublicationContract.SheetFilename).LongLength
            < PreviewPublicationContract.AVtt,
            "the largest strip does not fit inside the WebVTT ceiling.");
    }

    /// <summary>
    /// The full sheet is the geometry ADR 0064 measured against: 6400 by 3600,
    /// near-square rather than a filmstrip.
    /// </summary>
    [Fact]
    public void The_full_sheet_is_the_geometry_the_contract_measured()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromHours(2));

        Assert.Equal((20, 20), (plan.Columns, plan.Rows));
        Assert.Equal((6400, 3600), (plan.Width, plan.Height));
    }

    /// <summary>
    /// The cues name a fixed string and never the file the sheet was made from.
    /// The parser discards it, which is exactly why nothing may be put there.
    /// </summary>
    [Fact]
    public void The_cues_name_the_fixed_filename_and_nothing_local()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(600));
        var vtt = Encoding.UTF8.GetString(plan.Vtt(PreviewPublicationContract.SheetFilename));

        Assert.StartsWith("WEBVTT", vtt, StringComparison.Ordinal);
        Assert.Contains($"{PreviewPublicationContract.SheetFilename}#xywh=0,0,320,180", vtt, StringComparison.Ordinal);
        Assert.DoesNotContain("/", vtt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first frame is half an interval in rather than at zero, which is the
    /// difference between a strip that opens on the picture and one that opens
    /// on the black frame most files begin with.
    /// </summary>
    [Fact]
    public void The_first_frame_is_half_an_interval_in()
    {
        var plan = SpriteSheetPlan.For(TimeSpan.FromSeconds(1200));

        Assert.Equal(TimeSpan.FromSeconds(10), plan.Interval);
        Assert.Equal(TimeSpan.FromSeconds(5), plan.FirstFrameAt);
    }
}
