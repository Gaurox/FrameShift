namespace FrameShift.Windows.AI;

/// <summary>DPI-aware video variant of the shared upscale model and scale picker.</summary>
public sealed class UpscaleVideoPickerForm : UpscaleImagePickerForm
{
    public UpscaleVideoPickerForm(
        string sourceLabel,
        int sourceWidth,
        int sourceHeight,
        bool allowCustomSize,
        string? initialModelId = null)
        : base(
            sourceLabel,
            sourceWidth,
            sourceHeight,
            allowCustomSize,
            initialModelId,
            videoMode: true)
    {
    }
}
