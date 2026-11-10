using System.Runtime.InteropServices;

namespace FrameShift.ShellMenu;

internal static unsafe class MenuIcons
{
    // Native AOT already pins the module to the host lifetime. Keep a bounded process-lifetime
    // cache so bitmaps stay valid until the host destroys its menus, including repeated queries.
    private static readonly Dictionary<string, nint> Bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, Bits;
        internal uint Compression, ImageSize;
        internal int XPels, YPels;
        internal uint Used, Important, Color;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImageW(nint instance, string path, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, in BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("kernel32.dll")] private static extern int WideCharToMultiByte(uint page, uint flags, char* text, int count, byte* output, int capacity, nint fallback, nint usedFallback);

    internal static nint Get(string? path, string executable)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return 0;
            var full = Path.GetFullPath(path);
            var assets = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "Assets")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return 0;
            // Read only the caller thread's own active window; never activate or inspect
            // another application's window. The headless probe uses system metrics.
            var owner = GetActiveWindow();
            var dpi = owner == 0 ? 0 : GetDpiForWindow(owner);
            var size = Math.Clamp(dpi == 0 ? GetSystemMetrics(49) : GetSystemMetricsForDpi(49, dpi), 16, 64);
            var key = full + "|" + size;
            lock (Sync)
            {
                if (Bitmaps.TryGetValue(key, out var cached)) return cached;
                if (Bitmaps.Count >= 96) return 0;
                var bitmap = Load(full, size);
                if (bitmap != 0) Bitmaps.Add(key, bitmap);
                return bitmap;
            }
        }
        catch (Exception) { return 0; } // A missing icon must not hide an action.
    }

    private static nint Load(string path, int size)
    {
        var icon = LoadImageW(0, path, 1, size, size, 0x10);
        if (icon == 0) return 0;
        nint bitmap = 0, dc = 0, previous = 0;
        try
        {
            var info = new BitmapInfo { Size = 40, Width = size, Height = -size, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(0, in info, 0, out var bits, 0, 0);
            if (bitmap == 0) return 0;
            new Span<byte>((void*)bits, size * size * 4).Clear();
            dc = CreateCompatibleDC(0);
            if (dc == 0) return 0;
            previous = SelectObject(dc, bitmap);
            if (previous == 0 || previous == -1 || !DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, 3)) return 0;
            var result = bitmap;
            bitmap = 0;
            return result;
        }
        finally
        {
            if (previous != 0 && previous != -1) SelectObject(dc, previous);
            if (dc != 0) DeleteDC(dc);
            if (bitmap != 0) DeleteObject(bitmap);
            DestroyIcon(icon);
        }
    }

    internal static byte[] AnsiBytes(string text)
    {
        fixed (char* value = text)
        {
            var length = WideCharToMultiByte(0, 0, value, text.Length, null, 0, 0, 0);
            var bytes = new byte[length];
            fixed (byte* output = bytes) WideCharToMultiByte(0, 0, value, text.Length, output, length, 0, 0);
            return bytes;
        }
    }
}
