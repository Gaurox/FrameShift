using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameShift.Windows.Helpers;

public static class FrameShiftWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBeforeWindows10Version1903 = 19;
    private static readonly ConditionalWeakTable<Form, ChromeResources> Resources = new();

    public static void Apply(Form form, string title)
        => Apply(form, title, IconPaths.AppIcon, string.Empty);

    public static void Apply(Form form, string title, string preferredIconPath, string fallbackIconPath)
    {
        ObjectDisposedException.ThrowIf(form.IsDisposed, form);
        var resources = Resources.GetValue(form, owner => new ChromeResources(owner));
        form.Text = title;
        form.ShowIcon = true;
        RefreshTheme(form);
        var path = FrameShiftUiPainter.ResolveIconPath(preferredIconPath, fallbackIconPath);
        if (!string.IsNullOrEmpty(path)) resources.ApplyIcon(form, Path.GetFullPath(path));
    }

    public static void RefreshTheme(Form form)
    {
        form.ForeColor = FrameShiftTheme.TextPrimary;

        if (form.IsHandleCreated)
        {
            TryApplyTitleBarTheme(form.Handle);
        }
    }

    // Only icons loaded here are owned here. A caller-assigned Icon must never be disposed by this helper.
    private sealed class ChromeResources
    {
        private Icon? _ownedIcon;
        private string? _iconPath;

        public ChromeResources(Form form)
        {
            form.HandleCreated += OnHandleCreated;
            form.Disposed += OnDisposed;
        }

        public void ApplyIcon(Form form, string path)
        {
            if (string.Equals(_iconPath, path, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(form.Icon, _ownedIcon)) return;
            var next = new Icon(path);
            try { form.Icon = next; }
            catch { next.Dispose(); throw; }
            var previous = _ownedIcon;
            _ownedIcon = next;
            _iconPath = path;
            previous?.Dispose();
        }

        private void OnHandleCreated(object? sender, EventArgs e) => TryApplyTitleBarTheme(((Form)sender!).Handle);

        private void OnDisposed(object? sender, EventArgs e)
        {
            var form = (Form)sender!;
            form.HandleCreated -= OnHandleCreated;
            form.Disposed -= OnDisposed;
            _ownedIcon?.Dispose();
            _ownedIcon = null;
            Resources.Remove(form);
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
