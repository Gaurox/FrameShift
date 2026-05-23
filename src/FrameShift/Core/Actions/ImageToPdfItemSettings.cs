using System.Drawing;

namespace FrameShift.Core.Actions;

public sealed class ImageToPdfItemSettings
{
    public string SourcePath { get; set; } = string.Empty;

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; } = 1f;

    public float Height { get; set; } = 1f;

    public int RotationQuarterTurns { get; set; }

    public double RotationAngleDegrees { get; set; }

    public ImageToPdfCropSettings Crop { get; set; } = ImageToPdfCropSettings.CreateDefault();

    public RectangleF ToRectangleF()
    {
        return new RectangleF(X, Y, Width, Height);
    }

    public double GetRotationAngleDegrees()
    {
        return Math.Abs(RotationAngleDegrees) > 0.001
            ? RotationAngleDegrees
            : (RotationQuarterTurns * 90.0);
    }

    public ImageToPdfCropSettings GetCrop()
    {
        Crop ??= ImageToPdfCropSettings.CreateDefault();
        return Crop;
    }
}
