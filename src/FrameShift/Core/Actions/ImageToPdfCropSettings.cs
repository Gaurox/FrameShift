namespace FrameShift.Core.Actions;

public sealed class ImageToPdfCropSettings
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Right { get; set; } = 1.0;

    public double Bottom { get; set; } = 1.0;

    public static ImageToPdfCropSettings CreateDefault()
    {
        return new ImageToPdfCropSettings();
    }
}
