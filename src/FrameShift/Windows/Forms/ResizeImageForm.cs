namespace FrameShift.Windows.Forms;

public sealed class ResizeImageForm : ResizeMediaFormBase
{
    public ResizeImageForm(string sourcePath, int originalWidth, int originalHeight)
        : base("Resize Image", sourcePath, originalWidth, originalHeight, "▣")
    {
    }
}
