// Included ONLY in the separate Native AOT fixture build, never in the shipped DLL.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FrameShift.ShellMenu;

internal static unsafe class ProbeFixture
{
    private static string? _key, _executable;
    private static string _launch = "";

    [UnmanagedCallersOnly(EntryPoint = "FrameShiftProbeConfigure", CallConvs = [typeof(CallConvStdcall)])]
    internal static int Configure(char* key, char* executable)
    {
        try
        {
            var name = new string(key);
            if (!name.StartsWith(@"Software\FrameShift.Tests\ShellMenu\", StringComparison.Ordinal)) return ComAbi.InvalidArgument;
            _key = name;
            _executable = new string(executable);
            _launch = "";
            return 0;
        }
        catch (Exception) { return ComAbi.InvalidArgument; }
    }

    internal static MenuHandler CreateHandler() => new(paths =>
    {
        using var root = Registry.CurrentUser.OpenSubKey(_key ?? throw new InvalidOperationException());
        return root is null ? new(_executable!, []) : MenuMetadata.Read(root, _executable!, paths);
    }, start =>
    {
        if (start.UseShellExecute || !start.CreateNoWindow) throw new InvalidOperationException("Unsafe launch flags.");
        // NUL cannot occur in a file name or CLI argument. No process is started by this fixture.
        _launch = string.Join('\0', new[] { start.FileName }.Concat(start.ArgumentList));
    });

    [UnmanagedCallersOnly(EntryPoint = "FrameShiftProbeReadLaunch", CallConvs = [typeof(CallConvStdcall)])]
    internal static int ReadLaunch(char* output, int capacity)
    {
        if (output == null || capacity <= _launch.Length) return ComAbi.InsufficientBuffer;
        _launch.AsSpan().CopyTo(new Span<char>(output, capacity));
        output[_launch.Length] = '\0';
        return _launch.Length;
    }
}
