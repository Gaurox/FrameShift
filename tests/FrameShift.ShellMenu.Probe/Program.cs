using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FrameShift.ShellMenu;

internal static unsafe partial class Program
{
    // Independent contract for the 18 installed video commands, including the flattened variants.
    private static readonly (string Path, string Resource, string Action, string? Mode, bool Single)[] Commands =
    [
        ("add_subtitles_video", "add_subtitles_video", "add-subtitles-video", null, true),
        ("change_video_speed", "change_video_speed", "change-video-speed", null, true),
        ("compress_video", "compress_video", "compress-video", null, false),
        ("convert_video", "convert_video", "convert-video", null, false),
        ("create_gif", "create_gif", "create-gif", null, true),
        ("crop_video", "crop_video", "crop-video", null, true),
        ("cut_video", "cut_video", "cut-video", null, true),
        ("extract_all_frames", "extract_all_frames", "extract-frames", "all", false),
        ("extract_audio", "extract_audio", "extract-audio", null, false),
        (@"extract_specific_frames\shell\first_frame", "extract_first_frame", "extract-frames", "first", false),
        (@"extract_specific_frames\shell\last_frame", "extract_last_frame", "extract-frames", "last", false),
        (@"extract_specific_frames\shell\keyframes", "extract_keyframes", "extract-frames", "keyframes", false),
        ("interpolate_video", "interpolate_video", "interpolate-video", null, true),
        ("join_videos", "join_videos", "join-videos", null, false),
        ("media_info", "media_info", "media-info", null, true),
        ("remove_audio", "remove_audio", "remove-audio", null, false),
        ("resize_video", "resize_video", "resize-video", null, true),
        ("rotate_flip_video", "rotate_flip_video", "rotate-flip-video", null, true)
    ];
    private static readonly List<object> Results = [];
    private static int _failures;
    private static string _fixtureKey = "", _executable = "", _sourceRoot = "";
    private static string[] _files = [];
    private static RegistryKey _root = null!;
    private static nint _factory, _handler;
    private static delegate* unmanaged[Stdcall]<char*, char*, int> _configure;
    private static delegate* unmanaged[Stdcall]<char*, int, int> _readLaunch;
    private static readonly StrategyBasedComWrappers Wrappers = new();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 8 || args[0] != "--native" || args[2] != "--fixture" || args[4] != "--report" || args[6] != "--source-root") return 2;
        var native = Path.GetFullPath(args[1]);
        var fixture = Path.GetFullPath(args[3]);
        var report = Path.GetFullPath(args[5]);
        _sourceRoot = Path.GetFullPath(args[7]);
        if (File.Exists(report)) { Console.Error.WriteLine("Choose a new report path."); return 2; }
        var workspace = Path.Combine(_sourceRoot, "scratch", "shell-menu-tests", Guid.NewGuid().ToString("N"));
        _fixtureKey = @"Software\FrameShift.Tests\ShellMenu\" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(workspace);
            _executable = Path.Combine(workspace, "Application été", "FrameShift.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(_executable)!);
            File.WriteAllBytes(_executable, []); // Never executed; the native fixture captures the launch.
            _files = [Path.Combine(workspace, "Vidéo été 1.mp4"), Path.Combine(workspace, "Deuxième vidéo.mkv")];
            foreach (var path in _files) File.WriteAllBytes(path, []);
            var icon = Path.Combine(Path.GetDirectoryName(_executable)!, "Assets", "Icons", "app", "app.ico");
            Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
            File.Copy(Path.Combine(_sourceRoot, "src/FrameShift/Assets/Icons/app/app.ico"), icon);
            _root = Registry.CurrentUser.CreateSubKey(_fixtureKey);

            Run("Production DLL is native x64 and contains no probe/registration exports", () =>
            {
                using var file = File.OpenRead(native);
                using var pe = new PEReader(file);
                Require(pe.PEHeaders.CorHeader is null && pe.PEHeaders.CoffHeader.Machine == Machine.Amd64, "Not native x64.");
                var library = NativeLibrary.Load(native);
                foreach (var name in new[] { "FrameShiftProbeConfigure", "FrameShiftProbeReadLaunch", "DllRegisterServer", "DllUnregisterServer" })
                    Require(!NativeLibrary.TryGetExport(library, name, out _), "Unexpected export: " + name);
                var unload = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(library, "DllCanUnloadNow");
                Require(unload() == 1, "AOT lifetime contract changed.");
                var get = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)NativeLibrary.GetExport(library, "DllGetClassObject");
                var clsid = ComAbi.ClassId; var iid = ComAbi.FactoryId; nint factory = 0;
                Check(get(&clsid, &iid, &factory));
                ComAbi.Release(factory);
                clsid = Guid.Empty;
                Require(get(&clsid, &iid, &factory) == ComAbi.ClassUnavailable && factory == 0, "Unknown class accepted.");
            });

            var testLibrary = NativeLibrary.Load(fixture);
            _configure = (delegate* unmanaged[Stdcall]<char*, char*, int>)NativeLibrary.GetExport(testLibrary, "FrameShiftProbeConfigure");
            _readLaunch = (delegate* unmanaged[Stdcall]<char*, int, int>)NativeLibrary.GetExport(testLibrary, "FrameShiftProbeReadLaunch");
            var getFixture = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)NativeLibrary.GetExport(testLibrary, "DllGetClassObject");
            var classId = ComAbi.ClassId; var factoryId = ComAbi.FactoryId;
            fixed (nint* output = &_factory) Check(getFixture(&classId, &factoryId, output));
            var contextId = ComAbi.ContextMenuId;
            fixed (nint* output = &_handler)
                Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)_factory)[3])(_factory, 0, &contextId, output));

            foreach (var language in new[] { "en", "zh-CN", "es", "pt-BR", "ru" })
                Run("18 flat localized commands and canonical verbs: " + language, () =>
                {
                    var captions = Populate(language);
                    Initialize([_files[0]]);
                    WithMenu(menu =>
                    {
                        NativeMenu.Insert(menu, 0, "Host before", 80);
                        NativeMenu.Insert(menu, 1, "Host after", 81);
                        Require(Query(menu, 1, 1000, 1017) == 18, "Expected 18 IDs.");
                        Require(NativeMenu.GetMenuItemCount(menu) == 3, "Host entries changed.");
                        var group = NativeMenu.Read(menu, 1);
                        Require(group.Caption == "FrameShift" && group.Submenu != 0, "Parent missing.");
                        Require(NativeMenu.GetMenuItemCount(group.Submenu) == 18, "Menu truncated.");
                        for (uint index = 0; index < 18; index++)
                        {
                            var item = NativeMenu.Read(group.Submenu, index);
                            Require(item.Caption == captions[index] && item.Id == 1000 + index && item.Submenu == 0, "Caption/ID/flattening mismatch at " + index);
                            Require((item.State & 3) == (index == 13 ? 3u : 0u), "Incorrect single-selection state at " + index);
                            Require(ReadVerb(index) == Verb((int)index), "Canonical verb mismatch.");
                        }
                        var image = NativeMenu.Item.Create(0x80);
                        Require(NativeMenu.GetMenuItemInfo(group.Submenu, 0, true, ref image) && image.Bitmap != 0, "Native icon missing.");
                    });
                    Require(LastLaunch().Length == 0, "Menu consultation launched an action.");
                });

            Run("All 18 actions route exact arguments without executing a process", () =>
            {
                Populate("en");
                for (var index = 0; index < Commands.Length; index++)
                {
                    var selected = index == 13 ? _files : [_files[0]];
                    Initialize(selected);
                    WithMenu(menu =>
                    {
                        Require(Query(menu, 0, 50000, 50017) == 18, "Menu construction failed.");
                        var info = new Invocation { Size = (uint)sizeof(Invocation), Verb = index };
                        Check(Invoke(&info));
                    });
                    var expected = new List<string> { _executable, "--action", Commands[index].Action };
                    if (Commands[index].Mode is { } mode) expected.AddRange(["--frame-mode", mode]);
                    expected.AddRange(selected);
                    Require(LastLaunch().SequenceEqual(expected), "Wrong launch arguments for " + index);
                }
            });
            Run("Canonical ANSI and Unicode invocation routes Resize Video", () =>
            {
                Populate("en"); Initialize([_files[0]]);
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 100, 117) == 18, "Missing menu.");
                    var verb = "frameshift.resize-video";
                    fixed (char* wide = verb)
                    {
                        var info = new Invocation { Size = (uint)sizeof(Invocation), Mask = 0x4000, VerbW = (nint)wide };
                        Check(Invoke(&info));
                    }
                    var ansi = Marshal.StringToCoTaskMemUTF8(verb);
                    try { var info = new Invocation { Size = 56, Verb = ansi }; Check(Invoke(&info)); }
                    finally { Marshal.FreeCoTaskMem(ansi); }
                    Require(LastLaunch()[2] == "resize-video", "Wrong canonical action.");
                });
            });
            Run("Multiple selected videos preserve batch inputs and disable single-file editors", () =>
            {
                Populate("en"); Initialize(_files);
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 100, 117) == 18, "Menu missing.");
                    var child = NativeMenu.Read(menu, 0).Submenu;
                    for (uint index = 0; index < 18; index++)
                        Require((NativeMenu.Read(child, index).State & 3) == (Commands[index].Single ? 3u : 0u), "Incorrect multiselection state.");
                    var disabled = new Invocation { Size = 56, Verb = 16 };
                    Require(Invoke(&disabled) == ComAbi.InvalidArgument, "Multi-file resize accepted.");
                    var batch = new Invocation { Size = 56, Verb = 3 };
                    Check(Invoke(&batch));
                    Require(LastLaunch().Skip(3).SequenceEqual(_files), "Selection was truncated or reordered.");
                });
            });
            Run("Component-only and core-only metadata produce exact subsets", () =>
            {
                Populate("en");
                foreach (var command in Commands.Where(command => command.Path != "resize_video"))
                    foreach (var extension in new[] { ".mp4", ".mkv" })
                        _root.DeleteSubKeyTree(extension + @"\shell\FrameShift\shell\" + command.Path, false);
                Initialize([_files[0]]);
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 100, 100) == 1, "Resize-only component not respected.");
                    Require(ReadVerb(0) == "frameshift.resize-video", "Subset offset mapping is incorrect.");
                });
                _root.DeleteSubKeyTree(".mp4", false);
                WithMenu(menu => Require(Query(menu, 0, 100, 117) == 0 && NativeMenu.GetMenuItemCount(menu) == 0, "Core-only menu exists."));
            });
            Run("Foreign installation, wrong schema and foreign command are ignored", () =>
            {
                Populate("en"); Initialize([_files[0]]);
                _root.SetValue("ExecutablePath", @"C:\Other\FrameShift.exe");
                WithMenu(menu => Require(Query(menu, 0, 100, 117) == 0, "Foreign installation accepted."));
                _root.SetValue("ExecutablePath", _executable);
                _root.SetValue("SchemaVersion", 99);
                WithMenu(menu => Require(Query(menu, 0, 100, 117) == 0, "Unknown schema accepted."));
                _root.SetValue("SchemaVersion", 2);
                using var foreign = _root.OpenSubKey(@".mp4\shell\FrameShift\shell\resize_video\command", true);
                foreign!.SetValue("", "\"C:\\Other.exe\" --action resize-video \"%1\"");
                WithMenu(menu => Require(Query(menu, 0, 100, 117) == 17, "Foreign command accepted."));
            });
            Run("Unsupported/mixed selections clear prior state", () =>
            {
                Populate("en");
                Require(Initialize([_files[0], Path.Combine(workspace, "image.png")], false) < 0, "Mixed selection accepted.");
                WithMenu(menu => Require(Query(menu, 0, 100, 117) == 0, "Stale selection retained."));
                Require(Initialize(["relative.mp4"], false) < 0, "Relative path accepted.");
                Require(Initialize(Enumerable.Repeat(_files[0], 1001).ToArray(), false) < 0, "Unbounded selection accepted.");
            });
            Run("Large selections disable actions rather than dropping files", () =>
            {
                Populate("en"); Initialize(Enumerable.Repeat(_files[0], 1000).ToArray());
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 100, 117) == 18, "Large selection menu failed.");
                    var child = NativeMenu.Read(menu, 0).Submenu;
                    for (uint index = 0; index < 18; index++) Require((NativeMenu.Read(child, index).State & 3) == 3, "Overlong command line enabled.");
                });
                Require(LastLaunch().Length == 0, "Large selection launched an action.");
            });
            Run("ID exhaustion/default-only requests never create a partial menu", () =>
            {
                Populate("en"); Initialize([_files[0]]);
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 100, 116) == ComAbi.InsufficientBuffer, "17-ID range accepted.");
                    Require(Query(menu, 0, 100, 117, 1) == 0 && NativeMenu.GetMenuItemCount(menu) == 0, "Default-only menu added.");
                });
            });
            Run("Native resources remain bounded across 200 success and 100 failure paths", () =>
            {
                Populate("en"); Initialize([_files[0]]);
                WithMenu(menu => Check(Query(menu, 0, 100, 117)));
                var process = NativeMenu.GetCurrentProcess();
                var user = NativeMenu.GetGuiResources(process, 1); var gdi = NativeMenu.GetGuiResources(process, 0);
                for (var index = 0; index < 200; index++) WithMenu(menu => Require(Query(menu, 0, 100, 117) == 18, "Repeated menu failed."));
                for (var index = 0; index < 100; index++) Require(Query(-1, 0, 100, 117) < 0, "Invalid parent accepted.");
                Require(NativeMenu.GetGuiResources(process, 1) == user && NativeMenu.GetGuiResources(process, 0) == gdi, "USER/GDI resources grew.");
            });
            RunUnifiedMenus();
        }
        catch (Exception error) { _failures++; Results.Add(new { Name = "Setup", Passed = false, Error = error.ToString() }); Console.Error.WriteLine(error); }
        finally
        {
            ComAbi.Release(_handler); ComAbi.Release(_factory);
            _root?.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_fixtureKey, false); // Only this run's random fixture key.
            // Native AOT libraries intentionally remain loaded until process exit.
        }
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        using (var file = new FileStream(report, FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(file, new { TimestampUtc = DateTimeOffset.UtcNow, Framework = RuntimeInformation.FrameworkDescription,
                ProductionSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))),
                Passed = Results.Count - _failures, Failed = _failures, Results,
                Scope = "Unregistered COM host; isolated HKCU test fixtures; no Explorer input, installation, or actual process launch" }, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"{Results.Count - _failures} passed; {_failures} failed. Report: {report}");
        return _failures == 0 ? 0 : 1;
    }

    private static string[] Populate(string language)
    {
        foreach (var extension in _root.GetSubKeyNames()) _root.DeleteSubKeyTree(extension, false);
        _root.SetValue("SchemaVersion", 2); _root.SetValue("ExecutablePath", _executable);
        var suffix = language == "en" ? "" : "." + language;
        var resource = XDocument.Load(Path.Combine(_sourceRoot, "src/FrameShift/Core/Localization/Strings" + suffix + ".resx"))
            .Root!.Elements("data").ToDictionary(item => (string)item.Attribute("name")!, item => item.Element("value")!.Value);
        var captions = Commands.Select(command => resource["ExplorerMenu_" + command.Resource]).ToArray();
        for (var index = 0; index < Commands.Length; index++)
        foreach (var extension in new[] { ".mp4", ".mkv" })
        {
            using var key = _root.CreateSubKey(extension + @"\shell\FrameShift\shell\" + Commands[index].Path);
            key.SetValue(Commands[index].Mode is not null and not "all" ? "FlatMUIVerb" : "MUIVerb", captions[index]);
            key.SetValue("Icon", Path.Combine(Path.GetDirectoryName(_executable)!, "Assets/Icons/app/app.ico"));
            using var commandKey = key.CreateSubKey("command");
            commandKey.SetValue("", '"' + _executable + "\" --action " + Commands[index].Action
                + (Commands[index].Mode is null ? "" : " --frame-mode " + Commands[index].Mode) + " \"%1\"");
        }
        fixed (char* key = _fixtureKey) fixed (char* executable = _executable) Check(_configure(key, executable));
        return captions;
    }

    private static int Initialize(string[] paths, bool check = true)
    {
        var data = new DropData(paths);
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(data, CreateComInterfaceFlags.None);
        nint init = 0, dataPointer = 0;
        try
        {
            Check(ComAbi.QueryInterface(_handler, new Guid("000214E8-0000-0000-C000-000000000046"), out init));
            Check(ComAbi.QueryInterface(unknown, new Guid("0000010E-0000-0000-C000-000000000046"), out dataPointer));
            var result = ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)(*(nint**)init)[3])(init, 0, dataPointer, 0);
            if (check) Check(result);
            return result;
        }
        finally { ComAbi.Release(init); ComAbi.Release(dataPointer); ComAbi.Release(unknown); }
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
    private static int Invoke(Invocation* info) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)(*(nint**)_handler)[4])(_handler, (nint)info);
    private static int Query(nint menu, uint position, uint first, uint last, uint flags = 0)
        => ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, int>)(*(nint**)_handler)[3])(_handler, menu, position, first, last, flags);
    private static string ReadVerb(uint index)
    {
        var buffer = stackalloc char[512];
        Check(((delegate* unmanaged[Stdcall]<nint, nuint, uint, nint, nint, uint, int>)(*(nint**)_handler)[5])(_handler, index, 4, 0, (nint)buffer, 512));
        return new string(buffer);
    }
    private static string Verb(int index) => "frameshift." + Commands[index].Action + (Commands[index].Mode is null ? "" : "." + Commands[index].Mode);
    private static string[] LastLaunch()
    {
        var buffer = new char[32768];
        fixed (char* output = buffer)
        {
            var count = _readLaunch(output, buffer.Length); Check(count);
            return count == 0 ? [] : new string(output, 0, count).Split('\0');
        }
    }
    private static void WithMenu(Action<nint> action)
    {
        var menu = NativeMenu.Create();
        try { action(menu); } finally { Require(NativeMenu.DestroyMenu(menu), "DestroyMenu failed."); }
    }
    private static void Run(string name, Action test)
    {
        try { test(); Results.Add(new { Name = name, Passed = true, Error = (string?)null }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failures++; Results.Add(new { Name = name, Passed = false, Error = error.Message }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Check(int result) => Require(result >= 0, "HRESULT 0x" + result.ToString("X8"));
}
