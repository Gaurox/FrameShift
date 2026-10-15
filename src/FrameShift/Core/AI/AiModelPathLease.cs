using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FrameShift.Core.AI;

// Windows directory handles pin every component, including ancestors above the models root.
// No delete sharing: a checked directory cannot be renamed while in use. For mutations,
// an exclusive guard keeps the leaf nonempty (Windows forbids adding a junction to a
// nonempty directory), allowing publication without blocking the kernel's parent write open.
internal sealed class AiModelPathLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    private FileStream? _guard;

    public static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
            throw new InvalidOperationException("Choose a regular, fully qualified AI models path.");
        if (path.Replace('/', '\\').Split('\\').Any(part => part.EndsWith(' ') || (part.EndsWith('.') && part is not "." and not "..")))
            throw new InvalidOperationException("AI model paths cannot contain ambiguous names.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var relative = full[(Path.GetPathRoot(full)?.Length ?? 0)..];
        if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Contains(':') || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidOperationException("AI model paths cannot contain streams or ambiguous names.");
        return full;
    }

    public static AiModelPathLease Acquire(string directory, bool create = false, bool allowFileUpdates = false)
    {
        var lease = new AiModelPathLease();
        try
        {
            var full = Normalize(directory);
            var components = new Stack<string>();
            var leasedPaths = new List<string>();
            for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
                components.Push(current);
            while (components.TryPop(out var component))
            {
                if (create && !Directory.Exists(component)) Directory.CreateDirectory(component);
                var handle = Open(component, 0x80000000, FileShare.Read, 3, 0x02000000);
                lease._handles.Add(handle);
                leasedPaths.Add(component);
                CheckRegular(handle, directory: true);
            }
            if (allowFileUpdates)
            {
                // The path is still pinned without write sharing while this guard is
                // created. CreateNew + DeleteOnClose never follows or removes another file.
                lease._guard = new FileStream(Path.Combine(full, $".frameshift-model-path-{Guid.NewGuid():N}.lease"),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                // Each ancestor contains a pinned child; the leaf contains our guard.
                // Keep them nonempty without blocking a download to a different child.
                for (var i = 0; i < lease._handles.Count; i++)
                {
                    var writableParent = Open(leasedPaths[i], 0x80000000, FileShare.ReadWrite, 3, 0x02000000);
                    try { CheckRegular(writableParent, directory: true); }
                    catch { writableParent.Dispose(); throw; }
                    lease._handles[i].Dispose();
                    lease._handles[i] = writableParent;
                }
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    public static void ValidateFile(string path)
    {
        Normalize(path);
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new InvalidOperationException("The AI model file is a filesystem link or a directory.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    internal static SafeFileHandle Open(string path, uint access, FileShare share, uint disposition, uint flags = 0)
    {
        var nativePath = GetNativePath(path);
        var handle = CreateFileW(nativePath, access, share, IntPtr.Zero, disposition, flags | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"Cannot safely open AI model path '{path}': {new Win32Exception(error).Message}", unchecked((int)(0x80070000u | (uint)error)));
        }
        return handle;
    }

    internal static string GetNativePath(string path)
    {
        var full = Normalize(path);
        return full.StartsWith(@"\\") ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

    internal static void CheckRegular(SafeFileHandle handle, bool directory = false)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out var info, 8))
            throw new IOException("Cannot inspect AI model path.", new Win32Exception(Marshal.GetLastWin32Error()));
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Attributes.HasFlag(FileAttributes.Directory) != directory)
            throw new InvalidOperationException("The AI models path contains a filesystem link. Choose a regular directory in Settings.");
    }

    // Rename the open, verified temporary itself; it never needs to be closed and reopened
    // by name. The old destination remains in place if Windows refuses the replacement.
    internal static void Publish(SafeFileHandle handle, string destination)
    {
        var name = System.Text.Encoding.Unicode.GetBytes(GetNativePath(destination));
        var rootOffset = IntPtr.Size;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + 4;
        // Windows' DOS-path conversion also needs a NUL terminator, even though the
        // structure carries a byte length. Never expose uninitialised trailing memory.
        var size = nameOffset + name.Length + 2;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.WriteByte(buffer, 1); // ReplaceIfExists
            Marshal.WriteInt32(buffer, lengthOffset, name.Length);
            Marshal.Copy(name, 0, buffer + nameOffset, name.Length);
            if (!SetFileInformationByHandle(handle, 3, buffer, (uint)size))
                throw new IOException("Cannot activate the downloaded AI model; the previous model was kept.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static void DeleteOnClose(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 1);
            if (!SetFileInformationByHandle(handle, 4, buffer, 4))
                throw new IOException("Cannot clean up the AI model temporary.", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        _guard?.Dispose();
        _guard = null;
        for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose();
        _handles.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public FileAttributes Attributes; public uint Tag; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTagInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, IntPtr info, uint size);
}
