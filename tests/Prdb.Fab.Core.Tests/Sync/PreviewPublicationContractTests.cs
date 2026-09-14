using Prdb.Fab.Core.Sync;

using Xunit;

namespace Prdb.Fab.Core.Tests.Sync;

/// <summary>
/// ADR 0064's generation bounds, held as arithmetic rather than as a table.
/// </summary>
public sealed class PreviewPublicationContractTests
{
    /// <summary>
    /// The interval is ten seconds where ten seconds fits between the two
    /// bounds, and the bounds win where it does not.
    /// </summary>
    [Theory]
    // A three-minute scene: too short for the interval, so the floor decides.
    [InlineData(180, 24)]
    // Twenty minutes at ten seconds a tile.
    [InlineData(1200, 120)]
    // Two hours: too long for the interval, so the ceiling decides.
    [InlineData(7200, 400)]
    // And a ten-hour file is still four hundred rather than a filmstrip.
    [InlineData(36000, 400)]
    public void The_tile_count_is_the_interval_between_its_two_bounds(int seconds, int tiles) =>
        Assert.Equal(tiles, PreviewPublicationContract.TilesFor(TimeSpan.FromSeconds(seconds)));

    /// <summary>
    /// A file with no Runtime is not eligible, so this is only ever reached for
    /// a file that has one. Answering with the floor rather than throwing keeps
    /// the arithmetic total — the eligibility rule is what refuses, in one
    /// place.
    /// </summary>
    [Fact]
    public void A_runtime_of_nothing_answers_with_the_floor() =>
        Assert.Equal(
            PreviewPublicationContract.FewestTiles,
            PreviewPublicationContract.TilesFor(TimeSpan.Zero));

    /// <summary>
    /// The grid is near-square at every size, which is what keeps a full sheet
    /// 6400x3600 rather than 3200x7200.
    /// </summary>
    [Fact]
    public void The_grid_is_near_square_and_holds_every_tile()
    {
        for (var tiles = PreviewPublicationContract.FewestTiles;
             tiles <= PreviewPublicationContract.MostTiles;
             tiles++)
        {
            var columns = PreviewPublicationContract.ColumnsFor(tiles);
            var rows = PreviewPublicationContract.RowsFor(tiles);

            Assert.True(columns * rows >= tiles, $"{tiles} tiles do not fit in {columns}x{rows}.");

            // Near-square: never more than one column wider than it is tall,
            // and never taller than it is wide.
            Assert.InRange(columns - rows, 0, 1);
        }
    }

    /// <summary>
    /// At the ceiling the sheet is the measured shape ADR 0064 quotes, which is
    /// the one dimension a decoder somewhere is going to have an opinion about.
    /// </summary>
    [Fact]
    public void A_full_sheet_is_the_measured_shape()
    {
        var tiles = PreviewPublicationContract.MostTiles;

        Assert.Equal(
            6400,
            PreviewPublicationContract.ColumnsFor(tiles) * PreviewPublicationContract.TileWidth);
        Assert.Equal(
            3600,
            PreviewPublicationContract.RowsFor(tiles) * PreviewPublicationContract.TileHeight);
    }

    /// <summary>
    /// Every tile is inside the file, in order, and the same file always gives
    /// the same positions — which is what makes a rerun after a crash the same
    /// bytes rather than a second picture.
    /// </summary>
    [Fact]
    public void The_positions_are_inside_the_file_in_order_and_the_same_every_time()
    {
        var runtime = TimeSpan.FromMinutes(20);
        var tiles = PreviewPublicationContract.TilesFor(runtime);

        var positions = Enumerable.Range(0, tiles)
            .Select(index => PreviewPublicationContract.TileAt(index, tiles, runtime))
            .ToArray();

        Assert.All(positions, at => Assert.InRange(at, TimeSpan.Zero, runtime));
        Assert.Equal(positions.Order(), positions);
        Assert.Equal(tiles, positions.Distinct().Count());

        Assert.Equal(
            positions,
            Enumerable.Range(0, tiles)
                .Select(index => PreviewPublicationContract.TileAt(index, tiles, runtime)));
    }

    /// <summary>
    /// The ceilings are ADR 0061's, read rather than restated: producing
    /// something this tool would itself refuse to display is the clearest
    /// possible sign a number is wrong.
    /// </summary>
    [Fact]
    public void What_is_produced_is_bounded_by_what_would_be_accepted()
    {
        Assert.Equal(UserPreviewContract.ASprite, PreviewPublicationContract.ASheet);
        Assert.Equal(UserPreviewContract.AVtt, PreviewPublicationContract.AVtt);
        Assert.Equal(UserPreviewContract.Tiles, PreviewPublicationContract.MostTiles);
    }
}
