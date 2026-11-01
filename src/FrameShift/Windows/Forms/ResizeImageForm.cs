namespace FrameShift.Windows.Forms;

public sealed class ResizeImageForm : ResizeMediaFormBase
{
    public ResizeImageForm(string sourcePath, int originalWidth, int originalHeight)
        : base(L.Get("ResizeImageForm_Resize_Image_86c18598"), sourcePath, originalWidth, originalHeight, "▣")
    {
    }
}
