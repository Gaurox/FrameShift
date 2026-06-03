using System.Linq;
using FrameShift.Core.AI.Upscale;
using Xunit;

namespace FrameShift.Tests;

public sealed class UpscaleTilerTests
{
    [Fact]
    public void Plan_SmallImage_ReturnsSingleFullTile()
    {
        var tiles = UpscaleTiler.Plan(400, 300, tileSize: 512, margin: 16);

        var tile = Assert.Single(tiles);
        Assert.Equal((0, 0, 400, 300), (tile.ReadX, tile.ReadY, tile.ReadW, tile.ReadH));
        Assert.Equal((0, 0, 400, 300), (tile.CoreX, tile.CoreY, tile.CoreW, tile.CoreH));
    }

    [Fact]
    public void Plan_ExactTileSize_StaysSingleTile()
    {
        var tiles = UpscaleTiler.Plan(512, 512, tileSize: 512, margin: 16);
        Assert.Single(tiles);
    }

    [Theory]
    [InlineData(700, 500, 256, 16)]
    [InlineData(1280, 720, 512, 16)]
    [InlineData(1000, 1000, 512, 32)]
    [InlineData(513, 513, 512, 16)] // just over a single tile
    public void Plan_CoresCoverEveryPixelExactlyOnce(int width, int height, int tileSize, int margin)
    {
        var tiles = UpscaleTiler.Plan(width, height, tileSize, margin);

        var coverage = new int[width, height];
        foreach (var t in tiles)
        {
            for (int y = t.CoreY; y < t.CoreY + t.CoreH; y++)
                for (int x = t.CoreX; x < t.CoreX + t.CoreW; x++)
                    coverage[x, y]++;
        }

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Assert.Equal(1, coverage[x, y]); // no gaps, no double-cover in core space
    }

    [Theory]
    [InlineData(700, 500, 256, 16)]
    [InlineData(1280, 720, 512, 16)]
    public void Plan_CoresSpanFullImageBounds(int width, int height, int tileSize, int margin)
    {
        var tiles = UpscaleTiler.Plan(width, height, tileSize, margin);

        Assert.Equal(width, tiles.Max(t => t.CoreX + t.CoreW));
        Assert.Equal(height, tiles.Max(t => t.CoreY + t.CoreH));
        Assert.All(tiles, t =>
        {
            Assert.True(t.CoreW > 0 && t.CoreH > 0);
        });
    }

    [Theory]
    [InlineData(700, 500, 256, 16)]
    [InlineData(1280, 720, 512, 16)]
    public void Plan_ReadRegionsStayWithinImageAndContainCore(int width, int height, int tileSize, int margin)
    {
        var tiles = UpscaleTiler.Plan(width, height, tileSize, margin);

        Assert.All(tiles, t =>
        {
            // within image bounds
            Assert.InRange(t.ReadX, 0, width);
            Assert.InRange(t.ReadY, 0, height);
            Assert.True(t.ReadX + t.ReadW <= width);
            Assert.True(t.ReadY + t.ReadH <= height);

            // read region fully contains its core (so the core can be cropped out of the SR result)
            Assert.True(t.ReadX <= t.CoreX);
            Assert.True(t.ReadY <= t.CoreY);
            Assert.True(t.ReadX + t.ReadW >= t.CoreX + t.CoreW);
            Assert.True(t.ReadY + t.ReadH >= t.CoreY + t.CoreH);
        });
    }

    [Fact]
    public void Plan_InteriorTile_HasContextOverlapOnInnerSides()
    {
        // 1280x720, tile 512, margin 16 -> core 480 -> 3 columns x 2 rows = 6 tiles.
        var tiles = UpscaleTiler.Plan(1280, 720, tileSize: 512, margin: 16);
        Assert.Equal(6, tiles.Count);

        // The middle column / top row tile is interior on its left and right edges:
        // it must read beyond its core on both inner sides (this is what removes seams).
        var interior = tiles.Single(t => t.CoreX == 480 && t.CoreY == 0);
        Assert.True(interior.ReadX < interior.CoreX, "interior tile should read left context");
        Assert.True(
            interior.ReadX + interior.ReadW > interior.CoreX + interior.CoreW,
            "interior tile should read right context");
        Assert.Equal(16, interior.CoreX - interior.ReadX); // exactly one margin of left overlap
    }

    [Fact]
    public void Plan_BorderTiles_DoNotReadOutsideTheImage()
    {
        var tiles = UpscaleTiler.Plan(1280, 720, tileSize: 512, margin: 16);

        // top-left tile: no context above or to the left of the image edge.
        var topLeft = tiles.Single(t => t.CoreX == 0 && t.CoreY == 0);
        Assert.Equal(0, topLeft.ReadX);
        Assert.Equal(0, topLeft.ReadY);

        // every tile on the right edge must read exactly up to the image width.
        foreach (var t in tiles.Where(t => t.CoreX + t.CoreW == 1280))
            Assert.Equal(1280, t.ReadX + t.ReadW);
    }

    [Fact]
    public void Plan_ScaledOutputDimensions_MatchSourceTimesScale()
    {
        const int scale = 4;
        var tiles = UpscaleTiler.Plan(1280, 720, tileSize: 512, margin: 16);

        // The written output is the union of scaled cores: it must exactly fill 4x the source.
        Assert.Equal(1280 * scale, tiles.Max(t => (t.CoreX + t.CoreW) * scale));
        Assert.Equal(720 * scale, tiles.Max(t => (t.CoreY + t.CoreH) * scale));
    }
}
