using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace FrameShift.ShellMenu;

[GeneratedComInterface]
[Guid("0000010E-0000-0000-C000-000000000046")]
internal partial interface IDataObject
{
    [PreserveSig] int GetData(nint format, nint medium);
    [PreserveSig] int GetDataHere(nint format, nint medium);
    [PreserveSig] int QueryGetData(nint format);
    [PreserveSig] int GetCanonicalFormatEtc(nint format, nint output);
    [PreserveSig] int SetData(nint format, nint medium, int release);
    [PreserveSig] int EnumFormatEtc(uint direction, nint output);
    [PreserveSig] int DAdvise(nint format, uint flags, nint sink, nint connection);
    [PreserveSig] int DUnadvise(uint connection);
    [PreserveSig] int EnumDAdvise(nint output);
}

[GeneratedComClass]
internal sealed partial class DropData(string[] paths) : IDataObject
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Medium { internal uint Type; internal nint Data, ReleaseObject; }
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint data);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint data);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint data);

    public unsafe int GetData(nint format, nint medium)
    {
        if (format == 0 || medium == 0 || *(short*)format != 15) return ComAbi.InvalidArgument;
        var text = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var data = GlobalAlloc(0x42, (nuint)(20 + text.Length));
        if (data == 0) return unchecked((int)0x8007000E);
        var memory = GlobalLock(data);
        if (memory == 0) { GlobalFree(data); return unchecked((int)0x80004005); }
        *(uint*)memory = 20; // DROPFILES.pFiles
        *(int*)(memory + 16) = 1; // DROPFILES.fWide
        text.CopyTo(new Span<byte>((void*)(memory + 20), text.Length));
        GlobalUnlock(data);
        *(Medium*)medium = new Medium { Type = 1, Data = data };
        return 0;
    }
    public int GetDataHere(nint format, nint medium) => ComAbi.NotImplemented;
    public int QueryGetData(nint format) => ComAbi.NotImplemented;
    public int GetCanonicalFormatEtc(nint format, nint output) => ComAbi.NotImplemented;
    public int SetData(nint format, nint medium, int release) => ComAbi.NotImplemented;
    public int EnumFormatEtc(uint direction, nint output) => ComAbi.NotImplemented;
    public int DAdvise(nint format, uint flags, nint sink, nint connection) => ComAbi.NotImplemented;
    public int DUnadvise(uint connection) => ComAbi.NotImplemented;
    public int EnumDAdvise(nint output) => ComAbi.NotImplemented;
}
