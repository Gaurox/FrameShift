using System;
using System.Collections.Generic;

namespace FrameShift.Core.AI.Upscale;

/// <summary>
/// Pure geometry for seamless tiled super-resolution.
///
/// Each tile reads an expanded region (a "core" block plus a margin of context on the inner sides),
/// runs the model, then only the core sub-rectangle is written to the output. Because every written
/// pixel came from a tile that had real image context around it (except at the true image borders),
/// there are no convolution-edge seams and no per-pixel blending buffers are required.
/// </summary>
internal static class UpscaleTiler
{
    internal readonly record struct TilePlan(
        int ReadX, int ReadY, int ReadW, int ReadH,
        int CoreX, int CoreY, int CoreW, int CoreH);

    /// <summary>
    /// Builds the list of tiles covering a <paramref name="width"/> x <paramref name="height"/> image.
    /// <paramref name="tileSize"/> is the maximum read size per tile; <paramref name="margin"/> is the
    /// context overlap (in source pixels) kept on each inner side.
    /// </summary>
    public static IReadOnlyList<TilePlan> Plan(int width, int height, int tileSize, int margin)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive.");
        if (margin < 0)
            throw new ArgumentOutOfRangeException(nameof(margin), "Margin must be non-negative.");

        var tiles = new List<TilePlan>();

        // Fast path: the whole image fits in a single tile — no margins needed.
        if (width <= tileSize && height <= tileSize)
        {
            tiles.Add(new TilePlan(0, 0, width, height, 0, 0, width, height));
            return tiles;
        }

        // Core block is the tile minus the context margin on both inner sides.
        var core = Math.Max(1, tileSize - 2 * margin);

        for (int coreY = 0; coreY < height; coreY += core)
        {
            int coreH = Math.Min(core, height - coreY);
            int readY = Math.Max(0, coreY - margin);
            int readBottom = Math.Min(height, coreY + coreH + margin);
            int readH = readBottom - readY;

            for (int coreX = 0; coreX < width; coreX += core)
            {
                int coreW = Math.Min(core, width - coreX);
                int readX = Math.Max(0, coreX - margin);
                int readRight = Math.Min(width, coreX + coreW + margin);
                int readW = readRight - readX;

                tiles.Add(new TilePlan(readX, readY, readW, readH, coreX, coreY, coreW, coreH));
            }
        }

        return tiles;
    }
}
