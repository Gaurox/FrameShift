using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace FrameShift.ShellMenu;

[GeneratedComInterface]
[Guid("000214E4-0000-0000-C000-000000000046")]
internal partial interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint menu, uint position, uint firstId, uint lastId, uint flags);
    [PreserveSig] int InvokeCommand(nint invocation);
    [PreserveSig] int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint capacity);
}

[GeneratedComInterface]
[Guid("000214E8-0000-0000-C000-000000000046")]
internal partial interface IShellExtInit
{
    [PreserveSig] int Initialize(nint folder, nint dataObject, nint registryKey);
}

[GeneratedComClass]
internal sealed partial class MenuHandler : IContextMenu, IShellExtInit
{
    private readonly Func<IReadOnlyList<string>, MenuSnapshot> _readMetadata;
    private readonly Action<ProcessStartInfo> _launch;
    private string[] _paths = [];
    private VisibleCommand[] _shown = [];
    private string _executable = "";

    internal MenuHandler() : this(MenuMetadata.Read, start => { using var process = Process.Start(start) ?? throw new IOException("Cannot launch FrameShift."); }) { }
    internal MenuHandler(Func<IReadOnlyList<string>, MenuSnapshot> readMetadata, Action<ProcessStartInfo> launch)
    { _readMetadata = readMetadata; _launch = launch; }

    public int Initialize(nint folder, nint dataObject, nint registryKey)
    {
        _paths = []; _shown = []; _executable = "";
        try { _paths = NativeSelection.ReadDrop(dataObject); return _paths.Length > 0 ? 0 : ComAbi.InvalidArgument; }
        catch (Exception) { return ComAbi.InvalidArgument; }
    }

    public int QueryContextMenu(nint menu, uint position, uint firstId, uint lastId, uint flags)
    {
        _shown = [];
        if ((flags & 1) != 0 || _paths.Length == 0) return 0;
        if (menu == 0 || lastId < firstId) return ComAbi.InvalidArgument;
        var children = new List<(nint Menu, string Caption, nint Icon)>();
        var attached = 0;
        try
        {
            var snapshot = _readMetadata(_paths);
            if (snapshot.Commands.Count == 0) return 0;
            if ((ulong)lastId - firstId + 1 < (uint)snapshot.Commands.Count) return ComAbi.InsufficientBuffer;
            _executable = snapshot.Executable;
            var shown = snapshot.Commands.OrderBy(command => command.Definition.Ai).ToArray();
            var offset = 0;
            foreach (var ai in new[] { false, true })
            {
                var commands = shown.Where(command => command.Definition.Ai == ai).ToArray();
                if (commands.Length == 0) continue;
                var rootIcon = MenuIcons.Get(Path.Combine(Path.GetDirectoryName(_executable)!,
                    ai ? "Assets/Icons/ai/frameshift_ai.ico" : "Assets/Icons/app/app.ico"), _executable);
                var child = NativeMenu.Create();
                try { children.Add((child, ai ? "FrameShift AI" : "FrameShift", rootIcon)); }
                catch { NativeMenu.DestroyMenu(child); throw; }
                for (var index = 0; index < commands.Length; index++)
                {
                    var command = commands[index];
                    NativeMenu.Insert(child, (uint)index, command.Caption, firstId + (uint)offset++);
                    NativeMenu.SetStateAndIcon(child, (uint)index, IsEnabled(command.Definition) ? 0u : 3u,
                        MenuIcons.Get(command.Icon, _executable));
                }
            }
            // One handler inserts both roots consecutively at the Shell's requested position.
            // Build all children first; never leave one root behind if the second insert fails.
            foreach (var child in children)
            {
                NativeMenu.Insert(menu, position + (uint)attached, child.Caption, submenu: child.Menu);
                attached++;
                NativeMenu.SetStateAndIcon(menu, position + (uint)attached - 1, 0, child.Icon);
            }
            _shown = shown;
            return _shown.Length;
        }
        catch (Exception)
        {
            for (var index = 0; index < attached; index++) NativeMenu.DeleteMenu(menu, position, 0x400); // MF_BYPOSITION
            return unchecked((int)0x80004005);
        }
        finally
        {
            // Attached popups belong to the host (or were destroyed by rollback above).
            for (var index = attached; index < children.Count; index++) NativeMenu.DestroyMenu(children[index].Menu);
        }
    }

    private bool IsEnabled(MenuCommand command)
    {
        try { MenuCommands.BuildLaunch(_executable, command, _paths); return true; }
        catch (ArgumentException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Invocation
    {
        internal uint Size, Mask;
        internal nint Window, Verb, Parameters, Directory;
        internal int Show;
        internal uint HotKey;
        internal nint Icon, Title, VerbW, ParametersW, DirectoryW, TitleW;
        internal int X, Y;
    }

    public unsafe int InvokeCommand(nint invocation)
    {
        try
        {
            if (invocation == 0 || _shown.Length == 0) return ComAbi.InvalidArgument;
            var info = (Invocation*)invocation;
            if (info->Size < (uint)Marshal.OffsetOf<Invocation>(nameof(Invocation.Title))) return ComAbi.InvalidArgument;
            var verb = info->Verb;
            var unicode = info->Size >= sizeof(Invocation) && (info->Mask & 0x4000) != 0;
            if (unicode && info->VerbW != 0) verb = info->VerbW;
            VisibleCommand? command;
            if ((nuint)verb <= ushort.MaxValue)
                command = (nuint)verb < (nuint)_shown.Length ? _shown[(int)verb] : null;
            else
            {
                var name = unicode ? Marshal.PtrToStringUni(verb) : Marshal.PtrToStringAnsi(verb);
                command = _shown.FirstOrDefault(item => item.Definition.Verb.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            if (command is null || !IsEnabled(command.Definition)) return ComAbi.InvalidArgument;
            if (_paths.Any(path => !File.Exists(path))) return ComAbi.InvalidArgument;
            _launch(MenuCommands.BuildLaunch(_executable, command.Definition, _paths));
            return 0;
        }
        catch (Exception) { return unchecked((int)0x80004005); }
    }

    public unsafe int GetCommandString(nuint offset, uint flags, nint reserved, nint text, uint capacity)
    {
        try
        {
            var valid = offset < (nuint)_shown.Length;
            if (flags is 2 or 6) return valid ? 0 : 1;
            if (!valid || text == 0 || capacity == 0 || capacity > int.MaxValue) return ComAbi.InvalidArgument;
            if (flags is not (0 or 1 or 4 or 5)) return ComAbi.NotImplemented;
            var value = (flags & 1) != 0 ? _shown[(int)offset].Caption : _shown[(int)offset].Definition.Verb;
            if ((flags & 4) != 0)
            {
                if (value.Length + 1 > capacity) return ComAbi.InsufficientBuffer;
                value.AsSpan().CopyTo(new Span<char>((void*)text, (int)capacity));
                ((char*)text)[value.Length] = '\0';
            }
            else
            {
                // Canonical verbs are ASCII; help text uses the active Windows ANSI codepage.
                var bytes = (flags & 1) == 0 ? Encoding.ASCII.GetBytes(value) : MenuIcons.AnsiBytes(value);
                if (bytes.Length + 1 > capacity) return ComAbi.InsufficientBuffer;
                bytes.CopyTo(new Span<byte>((void*)text, (int)capacity));
                ((byte*)text)[bytes.Length] = 0;
            }
            return 0;
        }
        catch (Exception) { return unchecked((int)0x80004005); }
    }
}
