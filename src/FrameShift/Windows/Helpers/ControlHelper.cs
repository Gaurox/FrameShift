using System.Reflection;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class ControlHelper
{
    public static void SetDoubleBuffered(Control control)
    {
        var property = control.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
        property?.SetValue(control, true, null);
    }
}
