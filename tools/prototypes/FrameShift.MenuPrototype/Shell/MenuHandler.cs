using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace FrameShift.MenuPrototype;

[GeneratedComInterface]
[Guid("000214E4-0000-0000-C000-000000000046")]
internal partial interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint menu, uint position, uint firstId, uint lastId, uint flags);
    [PreserveSig] int InvokeCommand(nint invocation);
    [PreserveSig] int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint capacity);
}

[GeneratedComClass]
internal sealed partial class MenuHandler : IContextMenu
{
    private bool _hasMenu;

    public int QueryContextMenu(nint menu, uint position, uint firstId, uint lastId, uint flags)
    {
        // A rejected query invalidates old offsets; no truncated or stale action list.
        _hasMenu = false;
        if ((flags & 1) != 0) return 0; // CMF_DEFAULTONLY
        if (menu == 0 || lastId < firstId) return ComAbi.InvalidArgument;
        if ((ulong)lastId - firstId + 1 < (uint)VideoCommands.All.Length) return ComAbi.InsufficientBuffer;

        nint child = 0;
        try
        {
            child = NativeMenu.Create();
            for (var index = 0; index < VideoCommands.All.Length; index++)
                NativeMenu.Insert(child, (uint)index, VideoCommands.All[index].Caption, firstId + (uint)index);

            NativeMenu.Insert(menu, position, "FrameShift", submenu: child);
            child = 0; // The caller's parent HMENU now owns the submenu.
            _hasMenu = true;
            return VideoCommands.All.Length; // S_OK with the number of command IDs consumed.
        }
        catch (Exception)
        {
            return unchecked((int)0x80004005);
        }
        finally
        {
            if (child != 0) NativeMenu.DestroyMenu(child);
        }
    }

    // Intentionally disabled, including canonical-string invocation: no process can be launched.
    public int InvokeCommand(nint invocation) => ComAbi.NotImplemented;

    public unsafe int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint capacity)
    {
        try
        {
            var valid = _hasMenu && offset < (nuint)VideoCommands.All.Length;
            if (flags is 2 or 6) return valid ? 0 : 1; // GCS_VALIDATEA/W
            if (!valid) return ComAbi.InvalidArgument;
            if (flags is not (0 or 4)) return ComAbi.NotImplemented; // GCS_VERBA/W only
            if (text == 0 || capacity == 0 || capacity > int.MaxValue) return ComAbi.InvalidArgument;

            var verb = VideoCommands.All[(int)offset].Verb;
            if ((uint)verb.Length + 1 > capacity) return ComAbi.InsufficientBuffer;
            if (flags == 4)
            {
                var target = new Span<char>((void*)text, (int)capacity);
                verb.AsSpan().CopyTo(target);
                target[verb.Length] = '\0';
            }
            else
            {
                var target = new Span<byte>((void*)text, (int)capacity);
                var length = Encoding.ASCII.GetBytes(verb, target);
                target[length] = 0;
            }
            return 0;
        }
        catch (Exception)
        {
            return unchecked((int)0x80004005);
        }
    }
}
