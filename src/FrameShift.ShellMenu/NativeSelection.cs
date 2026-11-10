using System.Runtime.InteropServices;
using System.Text;

namespace FrameShift.ShellMenu;

internal static unsafe class NativeSelection
{
    internal static readonly Guid InitId = new("000214E8-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    private struct Format { internal short Clipboard; internal nint Target; internal uint Aspect; internal int Index; internal uint Medium; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Medium { internal uint Type; internal nint Data, ReleaseObject; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(nint drop, uint index, StringBuilder? buffer, uint length);
    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref Medium medium);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetModuleHandleExW(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameW(nint module, StringBuilder file, uint size);

    internal static string[] ReadDrop(nint dataObject)
    {
        if (dataObject == 0) return [];
        var format = new Format { Clipboard = 15, Aspect = 1, Index = -1, Medium = 1 }; // CF_HDROP, DVASPECT_CONTENT, TYMED_HGLOBAL
        Medium medium = default;
        var getData = (delegate* unmanaged[Stdcall]<nint, Format*, Medium*, int>)(*(nint**)dataObject)[3];
        if (getData(dataObject, &format, &medium) < 0) return [];
        try
        {
            if (medium.Type != 1 || medium.Data == 0) return [];
            var count = DragQueryFileW(medium.Data, uint.MaxValue, null, 0);
            if (count is 0 or > 1000) return [];
            var paths = new string[count];
            for (uint index = 0; index < count; index++)
            {
                var length = DragQueryFileW(medium.Data, index, null, 0);
                if (length is 0 or > 32767) return [];
                var text = new StringBuilder((int)length + 1);
                if (DragQueryFileW(medium.Data, index, text, length + 1) != length) return [];
                paths[index] = text.ToString();
            }
            return MenuCommands.IsSupportedSelection(paths) ? paths : [];
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    internal static string InstallExecutable()
    {
        // Resolve THIS DLL, never Environment.ProcessPath (which would be the Shell host).
        var address = (nint)(delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)&ComExports.GetClassObject;
        if (!GetModuleHandleExW(6, address, out var module)) throw new InvalidOperationException("Cannot locate Shell module.");
        var text = new StringBuilder(32768);
        var length = GetModuleFileNameW(module, text, (uint)text.Capacity);
        if (length == 0 || length >= text.Capacity) throw new InvalidOperationException("Invalid Shell module path.");
        var binary = new FileInfo(text.ToString());
        var build = binary.Directory;
        if (binary.Name != "FrameShift.ShellMenu.dll" || build?.Parent?.Name != "Shell" || build.Parent.Parent is null)
            throw new InvalidOperationException("Shell module is outside its installed location.");
        return Path.Combine(build.Parent.Parent.FullName, "FrameShift.exe");
    }
}
