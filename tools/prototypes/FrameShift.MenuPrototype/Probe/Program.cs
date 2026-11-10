using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace FrameShift.MenuPrototype;

internal static unsafe class Program
{
    // Independent expected contract: intentionally not linked to the server's command table.
    private static readonly (string Caption, string Verb)[] Expected =
    [
        ("Add subtitles to video", "frameshift.add-subtitles-video"),
        ("Change video speed", "frameshift.change-video-speed"),
        ("Compress video", "frameshift.compress-video"),
        ("Convert video", "frameshift.convert-video"),
        ("Create GIF", "frameshift.create-gif"),
        ("Crop video", "frameshift.crop-video"),
        ("Cut video", "frameshift.cut-video"),
        ("Extract all frames", "frameshift.extract-frames.all"),
        ("Extract audio", "frameshift.extract-audio"),
        ("Extract first frame", "frameshift.extract-frames.first"),
        ("Extract last frame", "frameshift.extract-frames.last"),
        ("Extract keyframes", "frameshift.extract-frames.keyframes"),
        ("Interpolate video", "frameshift.interpolate-video"),
        ("Join videos", "frameshift.join-videos"),
        ("Media Info", "frameshift.media-info"),
        ("Remove audio", "frameshift.remove-audio"),
        ("Resize video", "frameshift.resize-video"),
        ("Rotate / Flip video", "frameshift.rotate-flip-video")
    ];

    private static readonly List<TestResult> Tests = [];
    private static readonly List<ObservedCommand> Observed = [];
    private static nint _library;
    private static nint _factory;
    private static nint _handler;
    private static delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int> _getClassObject;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--native" || args[2] != "--report")
        {
            Console.Error.WriteLine("Usage: Probe --native <native DLL> --report <new JSON report>");
            return 2;
        }

        var libraryPath = Path.GetFullPath(args[1]);
        var reportPath = Path.GetFullPath(args[3]);
        if (File.Exists(reportPath))
        {
            Console.Error.WriteLine("Report already exists; choose a new path. Nothing was overwritten.");
            return 2;
        }

        var headerIsNative = false;
        string? hash = null;
        try
        {
            Require(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "Windows x64 is required.");
            using (var stream = File.OpenRead(libraryPath))
            using (var pe = new PEReader(stream))
                headerIsNative = pe.PEHeaders.CorHeader is null && pe.PEHeaders.CoffHeader.Machine == Machine.Amd64;
            Require(headerIsNative, "The probe requires an actual native x64 DLL, not a managed assembly.");
            hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(libraryPath)));

            _library = NativeLibrary.Load(libraryPath);
            _getClassObject = (delegate* unmanaged[Stdcall]<Guid*, Guid*, nint*, int>)NativeLibrary.GetExport(_library, "DllGetClassObject");
            Check(GetFactory(ComAbi.ClassId, ComAbi.FactoryId, out _factory), "Get class factory");
            Check(CreateHandler(ComAbi.ContextMenuId, 0, out _handler), "Create IContextMenu");

            Run("Native DLL has no registration exports", () =>
            {
                Require(!NativeLibrary.TryGetExport(_library, "DllRegisterServer", out _), "Unexpected registration export.");
                Require(!NativeLibrary.TryGetExport(_library, "DllUnregisterServer", out _), "Unexpected unregistration export.");
                var unload = (delegate* unmanaged[Stdcall]<int>)NativeLibrary.GetExport(_library, "DllCanUnloadNow");
                Require(unload() == 1, "Native AOT must remain loaded until this probe exits.");
            });
            Run("COM class factory rejects wrong CLSID and interface", () =>
            {
                Require(GetFactory(Guid.Empty, ComAbi.FactoryId, out var wrongClass) == ComAbi.ClassUnavailable && wrongClass == 0, "Wrong CLSID accepted.");
                Require(GetFactory(ComAbi.ClassId, Guid.Empty, out var wrongInterface) == ComAbi.NoInterface && wrongInterface == 0, "Wrong factory interface accepted.");
            });
            Run("COM activation rejects aggregation and unsupported interfaces", () =>
            {
                Require(CreateHandler(ComAbi.ContextMenuId, 1, out var aggregate) == ComAbi.NoAggregation && aggregate == 0, "Aggregation accepted.");
                Require(CreateHandler(Guid.Empty, 0, out var unsupported) == ComAbi.NoInterface && unsupported == 0, "Unsupported interface accepted.");
            });
            Run("COM IUnknown identity and lifetime", () =>
            {
                nint first = 0, second = 0;
                try
                {
                    Check(ComAbi.QueryInterface(_handler, ComAbi.UnknownId, out first), "Get IUnknown");
                    Check(ComAbi.QueryInterface(first, ComAbi.UnknownId, out second), "Get identity again");
                    Require(first == second, "IUnknown identity differs.");
                }
                finally { ComAbi.Release(second); ComAbi.Release(first); }
            });
            Run("One FrameShift menu contains all 18 flat enabled commands", () => InspectMenu(1000, 1017, false));
            Run("The same command IDs work when inserted between host entries", () => InspectMenu(50000, 50017, true));
            Run("CMF_DEFAULTONLY does not add a menu", () =>
            {
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 100, 117, 1) == 0, "Default-only query consumed IDs.");
                    Require(NativeMenu.GetMenuItemCount(menu) == 0, "Default-only query changed menu.");
                    Require(ValidateVerb(0) == 1, "Old offsets remain valid.");
                });
            });
            Run("Insufficient ID ranges fail without hiding the last actions", () =>
            {
                foreach (var available in new uint[] { 1, 15, 16, 17 })
                    WithMenu(menu =>
                    {
                        Require(Query(menu, 0, 100, 100 + available - 1, 0) == ComAbi.InsufficientBuffer, "Insufficient ID range was accepted.");
                        Require(NativeMenu.GetMenuItemCount(menu) == 0, "A partial menu was inserted.");
                        Require(ValidateVerb(0) == 1, "Rejected query exposed a stale verb.");
                    });
            });
            Run("Invalid handles and reversed ID ranges are rejected", () =>
            {
                Require(Query(0, 0, 100, 117, 0) == ComAbi.InvalidArgument, "Null HMENU accepted.");
                WithMenu(menu =>
                {
                    Require(Query(menu, 0, 117, 100, 0) == ComAbi.InvalidArgument, "Reversed range accepted.");
                    Require(NativeMenu.GetMenuItemCount(menu) == 0, "Invalid query changed menu.");
                });
            });
            Run("Failed parent insertion cleans up the newly built submenu", () =>
            {
                var process = NativeMenu.GetCurrentProcess();
                var before = NativeMenu.GetGuiResources(process, 1);
                for (var index = 0; index < 100; index++)
                    Require(Query(-1, 0, 100, 117, 0) < 0, "Invalid parent HMENU accepted.");
                Require(ValidateVerb(0) == 1, "Failed insertion exposed stale offsets.");
                Require(NativeMenu.GetGuiResources(process, 1) == before, "Failed insertion leaked a submenu.");
            });
            Run("Canonical verbs support Unicode, ANSI and buffer validation", VerifyVerbBuffers);
            Run("Invoking by offset or by verb cannot launch any action", VerifyInvocationDisabled);
            Run("200 creation/destruction cycles preserve native USER/GDI resources", VerifyResourceLifetime);
            Run("Fresh COM instances can repeatedly build the full menu", () =>
            {
                for (var index = 0; index < 50; index++)
                {
                    nint fresh = 0;
                    try
                    {
                        Check(CreateHandler(ComAbi.ContextMenuId, 0, out fresh), "Create another instance");
                        var previous = _handler;
                        _handler = fresh;
                        try { InspectMenu(2000, 2017, false); }
                        finally { _handler = previous; }
                    }
                    finally { ComAbi.Release(fresh); }
                }
            });
        }
        catch (Exception error)
        {
            Tests.Add(new("Probe setup", false, error.ToString()));
            Console.Error.WriteLine(error);
        }
        finally
        {
            ComAbi.Release(_handler);
            ComAbi.Release(_factory);
            // No NativeLibrary.Free: unloading Native AOT DLLs is unsupported.
        }

        var report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Scope = "Registration-free COM/native-HMENU validation; no Explorer integration or visual rendering tested",
            Framework = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            NativeLibrary = libraryPath,
            NativeLibrarySha256 = hash,
            NativeX64PeVerified = headerIsNative,
            ParentCaption = "FrameShift",
            ExpectedCommandCount = Expected.Length,
            ObservedCommands = Observed,
            Passed = Tests.Count(test => test.Passed),
            Failed = Tests.Count(test => !test.Passed),
            Tests,
            Boundaries = new
            {
                RegistryWrites = false,
                ExplorerRegistration = false,
                WindowsShown = false,
                ActionsLaunched = false,
                InstalledFrameShiftModified = false,
                ProductionReady = false
            }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        using (var output = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write))
            JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"{report.Passed} passed; {report.Failed} failed. Native menu: {Observed.Count} flat commands.");
        Console.WriteLine($"Report: {reportPath}");
        return report.Failed == 0 ? 0 : 1;
    }

    private static void InspectMenu(uint first, uint last, bool betweenHostItems)
    {
        WithMenu(parent =>
        {
            var completeSnapshot = new List<ObservedCommand>();
            if (betweenHostItems)
            {
                NativeMenu.Insert(parent, 0, "Host before", 42);
                NativeMenu.Insert(parent, 1, "Host after", 43);
            }
            var position = betweenHostItems ? 1u : 0u;
            Require(Query(parent, position, first, last, 0) == Expected.Length, "Consumed ID count is not 18.");
            Require(NativeMenu.GetMenuItemCount(parent) == (betweenHostItems ? 3 : 1), "Unexpected parent menu count.");
            var group = NativeMenu.Read(parent, position);
            Require(group.Caption == "FrameShift" && group.Submenu != 0, "Single FrameShift group missing.");
            Require(NativeMenu.GetMenuItemCount(group.Submenu) == Expected.Length, "Menu truncated above/below 16 commands.");
            var ids = new HashSet<uint>();
            for (var index = 0; index < Expected.Length; index++)
            {
                var item = NativeMenu.Read(group.Submenu, (uint)index);
                Require(item.Caption == Expected[index].Caption, $"Caption mismatch at {index}: {item.Caption}");
                Require(item.Submenu == 0, $"Nested menu at {index}.");
                Require((item.State & 3) == 0, $"Disabled or grayed item at {index}.");
                Require(item.Id == first + index && ids.Add(item.Id), $"Duplicate/incorrect command ID at {index}.");
                Require(ReadVerb((uint)index, true) == Expected[index].Verb, $"Incorrect Unicode verb at {index}.");
                Require(ReadVerb((uint)index, false) == Expected[index].Verb, $"Incorrect ANSI verb at {index}.");
                Require(ValidateVerb((uint)index) == 0, $"Valid offset rejected at {index}.");
                completeSnapshot.Add(new(index, item.Caption, item.Id, ReadVerb((uint)index, true), false, true));
            }
            Require(ValidateVerb((uint)Expected.Length) == 1, "Invalid offset accepted.");
            if (betweenHostItems)
            {
                var before = NativeMenu.Read(parent, 0);
                var after = NativeMenu.Read(parent, 2);
                Require(before.Caption == "Host before" && before.Id == 42, "Host's first command changed.");
                Require(after.Caption == "Host after" && after.Id == 43, "Host's last command changed.");
            }
            if (Observed.Count == 0) Observed.AddRange(completeSnapshot);
        });
    }

    private static void VerifyVerbBuffers()
    {
        WithMenu(menu =>
        {
            Check(Query(menu, 0, 100, 117, 0), "Build menu");
            var text = stackalloc char[64];
            text[0] = 'X';
            Require(GetVerb(16, 4, (nint)text, 1) == ComAbi.InsufficientBuffer && text[0] == 'X', "Small buffer was modified or accepted.");
            Require(GetVerb(18, 4, (nint)text, 64) == ComAbi.InvalidArgument, "Offset out of range accepted.");
            Require(GetVerb(16, 4, 0, 64) == ComAbi.InvalidArgument, "Null text accepted.");
            Require(GetVerb(16, 4, (nint)text, 0) == ComAbi.InvalidArgument, "Zero capacity accepted.");
            Require(GetVerb(16, 99, (nint)text, 64) == ComAbi.NotImplemented, "Unsupported string flag accepted.");
            var exact = (uint)Expected[16].Verb.Length + 1;
            Check(GetVerb(16, 4, (nint)text, exact), "Exact-size Unicode buffer");
            Require(new string(text) == Expected[16].Verb, "Exact buffer lost Resize video verb.");
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Invocation
    {
        internal uint Size, Mask;
        internal nint Window, Verb, Parameters, Directory;
        internal int Show;
        internal uint HotKey;
        internal nint Icon;
    }

    private static void VerifyInvocationDisabled()
    {
        var invoke = (delegate* unmanaged[Stdcall]<nint, nint, int>)(*(nint**)_handler)[4];
        Require(invoke(_handler, 0) == ComAbi.NotImplemented, "Null invocation was not disabled.");
        var invocation = new Invocation { Size = (uint)sizeof(Invocation), Verb = 16 };
        Require(invoke(_handler, (nint)(&invocation)) == ComAbi.NotImplemented, "Offset invocation was not disabled.");
        var verb = Marshal.StringToCoTaskMemUTF8("frameshift.resize-video");
        try
        {
            invocation.Verb = verb;
            Require(invoke(_handler, (nint)(&invocation)) == ComAbi.NotImplemented, "String invocation was not disabled.");
        }
        finally { Marshal.FreeCoTaskMem(verb); }
    }

    private static void VerifyResourceLifetime()
    {
        WithMenu(menu => Check(Query(menu, 0, 100, 117, 0), "Warm up menu"));
        var process = NativeMenu.GetCurrentProcess();
        var userBefore = NativeMenu.GetGuiResources(process, 1);
        var gdiBefore = NativeMenu.GetGuiResources(process, 0);
        for (var index = 0; index < 200; index++)
            WithMenu(menu =>
            {
                Require(Query(menu, 0, 100, 117, 0) == 18, "Repeated menu query failed.");
                Require(NativeMenu.GetMenuItemCount(NativeMenu.Read(menu, 0).Submenu) == 18, "Repeated menu truncated.");
            });
        Require(NativeMenu.GetGuiResources(process, 1) == userBefore, "USER resources grew after DestroyMenu.");
        Require(NativeMenu.GetGuiResources(process, 0) == gdiBefore, "GDI resources grew after DestroyMenu.");
    }

    private static int GetFactory(Guid classId, Guid interfaceId, out nint instance)
    {
        instance = 0;
        fixed (nint* output = &instance) return _getClassObject(&classId, &interfaceId, output);
    }

    private static int CreateHandler(Guid id, nint outer, out nint instance)
    {
        instance = 0;
        fixed (nint* output = &instance)
            return ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)_factory)[3])(_factory, outer, &id, output);
    }

    private static int Query(nint menu, uint position, uint first, uint last, uint flags)
        => ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, int>)(*(nint**)_handler)[3])(_handler, menu, position, first, last, flags);

    private static int GetVerb(nuint offset, uint flags, nint text, uint length)
        => ((delegate* unmanaged[Stdcall]<nint, nuint, uint, nint, nint, uint, int>)(*(nint**)_handler)[5])(_handler, offset, flags, 0, text, length);

    private static int ValidateVerb(uint offset) => GetVerb(offset, 6, 0, 0);

    private static string ReadVerb(uint offset, bool unicode)
    {
        var text = stackalloc byte[512];
        new Span<byte>(text, 512).Fill(0x7F);
        Check(GetVerb(offset, unicode ? 4u : 0u, (nint)text, 256), "Read canonical verb");
        return unicode ? Marshal.PtrToStringUni((nint)text)! : Marshal.PtrToStringAnsi((nint)text)!;
    }

    private static void WithMenu(Action<nint> action)
    {
        var menu = NativeMenu.Create();
        try { action(menu); }
        finally { Require(NativeMenu.DestroyMenu(menu), "DestroyMenu failed."); }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Tests.Add(new(name, true, null));
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            Tests.Add(new(name, false, error.Message));
            Console.WriteLine($"FAIL {name}: {error.Message}");
        }
    }

    private static void Check(int result, string operation) => Require(result >= 0, $"{operation}: HRESULT 0x{result:X8}");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record TestResult(string Name, bool Passed, string? Error);
    private sealed record ObservedCommand(int Offset, string Caption, uint Id, string CanonicalVerb, bool HasSubmenu, bool Enabled);
}
