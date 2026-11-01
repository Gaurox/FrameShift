namespace FrameShift.Windows.Forms;

public sealed class ResizeVideoForm : ResizeMediaFormBase
{
    public ResizeVideoForm(string sourcePath, int originalWidth, int originalHeight)
        : base(L.Get("ResizeVideoForm_Resize_Video_0ffcaad3"), sourcePath, originalWidth, originalHeight, "▣")
    {
    }
}
