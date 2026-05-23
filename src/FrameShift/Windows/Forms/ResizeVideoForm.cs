namespace FrameShift.Windows.Forms;

public sealed class ResizeVideoForm : ResizeMediaFormBase
{
    public ResizeVideoForm(string sourcePath, int originalWidth, int originalHeight)
        : base("Resize Video", sourcePath, originalWidth, originalHeight, "▣")
    {
    }
}
