using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FrameShift.ShellMenu;

internal static class NativeMenu
{
    internal const uint Id = 0x0002;
    internal const uint Submenu = 0x0004;
    internal const uint State = 0x0001;
    internal const uint String = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Item
    {
        internal uint Size, Mask, Type, State, Id;
        internal nint Submenu, CheckedBitmap, UncheckedBitmap;
        internal nuint Data;
        internal nint Text;
        internal uint TextLength;
        internal nint Bitmap;

        internal static Item Create(uint mask) => new()
        {
            Size = (uint)Marshal.SizeOf<Item>(), Mask = mask
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteMenu(nint menu, uint position, uint flags);

    [DllImport("user32.dll", EntryPoint = "InsertMenuItemW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenuItem(nint menu, uint position, [MarshalAs(UnmanagedType.Bool)] bool byPosition, in Item item);

    [DllImport("user32.dll", EntryPoint = "GetMenuItemInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMenuItemInfo(nint menu, uint position, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref Item item);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll", EntryPoint = "SetMenuItemInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuItemInfo(nint menu, uint position, [MarshalAs(UnmanagedType.Bool)] bool byPosition, in Item item);

    internal static void SetStateAndIcon(nint menu, uint position, uint state, nint bitmap)
    {
        var item = Item.Create(State | 0x0080); // MIIM_BITMAP
        item.State = state;
        item.Bitmap = bitmap;
        _ = SetMenuItemInfo(menu, position, true, in item);
    }

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(nint process, uint flags);

    [DllImport("kernel32.dll")]
    internal static extern nint GetCurrentProcess();

    internal static nint Create()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return menu;
    }

    internal static unsafe void Insert(nint menu, uint position, string caption, uint id = 0, nint submenu = 0)
    {
        var item = Item.Create(String | (submenu == 0 ? Id : Submenu));
        item.Id = id;
        item.Submenu = submenu;
        fixed (char* text = caption)
        {
            item.Text = (nint)text;
            if (!InsertMenuItem(menu, position, true, in item))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    internal static unsafe (string Caption, uint Id, nint Submenu, uint State) Read(nint menu, uint position)
    {
        var item = Item.Create(String | Id | Submenu | State);
        // All prototype labels fit; a fixed bound also makes malformed results fail explicitly.
        var text = new char[512];
        fixed (char* buffer = text)
        {
            item.Text = (nint)buffer;
            item.TextLength = (uint)text.Length;
            if (!GetMenuItemInfo(menu, position, true, ref item))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (item.TextLength >= text.Length - 1) throw new InvalidOperationException("Menu caption exceeds probe buffer.");
            return (new string(buffer, 0, (int)item.TextLength), item.Id, item.Submenu, item.State);
        }
    }
}
