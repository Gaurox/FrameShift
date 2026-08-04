using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBeforeWindows10Version1903 = 19;

    public static void Apply(Form form, string title)
    {
        form.Text = title;
        form.ShowIcon = true;
        ApplyTitleBarTheme(form);
        RefreshTheme(form);

        if (File.Exists(IconPaths.AppIcon))
        {
            form.Icon = new Icon(IconPaths.AppIcon);
        }
    }

    public static void Apply(Form form, string title, string preferredIconPath, string fallbackIconPath)
    {
        form.Text = title;
        form.ShowIcon = true;
        ApplyTitleBarTheme(form);
        RefreshTheme(form);

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

    public static void RefreshTheme(Form form)
    {
        form.ForeColor = FrameShiftTheme.TextPrimary;

        if (form.IsHandleCreated)
        {
            TryApplyTitleBarTheme(form.Handle);
        }
    }

    private static void ApplyTitleBarTheme(Form form)
    {
        form.HandleCreated += (_, _) => TryApplyTitleBarTheme(form.Handle);
        if (form.IsHandleCreated)
        {
            TryApplyTitleBarTheme(form.Handle);
        }
    }

    private static void TryApplyTitleBarTheme(IntPtr handle)
    {
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var useDarkMode = FrameShiftTheme.EffectiveTheme == FrameShiftThemeMode.Dark ? 1 : 0;
            var result = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
            if (result != 0)
            {
                DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkModeBeforeWindows10Version1903,
                    ref useDarkMode,
                    sizeof(int));
            }
        }
        catch
        {
            // The standard title bar remains usable on unsupported Windows versions.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
