using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftWindowChrome
{
    public static void Apply(Form form, string title)
    {
        form.Text = title;
        form.ShowIcon = true;

        if (File.Exists(IconPaths.AppIcon))
        {
            form.Icon = new Icon(IconPaths.AppIcon);
        }
    }

    public static void Apply(Form form, string title, string preferredIconPath, string fallbackIconPath)
    {
        form.Text = title;
        form.ShowIcon = true;

        if (!string.IsNullOrWhiteSpace(preferredIconPath) && File.Exists(preferredIconPath))
        {
            form.Icon = new Icon(preferredIconPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallbackIconPath) && File.Exists(fallbackIconPath))
        {
            form.Icon = new Icon(fallbackIconPath);
        }
    }
}
