using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace FrameShift.MenuPrototype;

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, nint interfaceId, nint output);
    [PreserveSig] int LockServer(int locked);
}

[GeneratedComClass]
internal sealed partial class MenuClassFactory : IClassFactory
{
    public unsafe int CreateInstance(nint outer, nint interfaceId, nint output)
    {
        if (output == 0) return ComAbi.InvalidArgument;
        *(nint*)output = 0;
        if (outer != 0) return ComAbi.NoAggregation;
        if (interfaceId == 0) return ComAbi.InvalidArgument;
        return ComExports.Expose(new MenuHandler(), *(Guid*)interfaceId, (nint*)output);
    }

    public int LockServer(int locked) => 0;
}

internal static unsafe class ComExports
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = [typeof(CallConvStdcall)])]
    internal static int GetClassObject(Guid* classId, Guid* interfaceId, nint* output)
    {
        if (output == null) return ComAbi.InvalidArgument;
        *output = 0;
        if (classId == null || interfaceId == null) return ComAbi.InvalidArgument;
        if (*classId != ComAbi.ClassId) return ComAbi.ClassUnavailable;
        return Expose(new MenuClassFactory(), *interfaceId, output);
    }

    // Native AOT libraries cannot be unloaded. The probe deliberately leaves it loaded until exit.
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = [typeof(CallConvStdcall)])]
    internal static int CanUnloadNow() => 1; // S_FALSE

    internal static int Expose(object instance, Guid interfaceId, nint* output)
    {
        nint unknown = 0;
        try
        {
            unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
            var result = ComAbi.QueryInterface(unknown, interfaceId, out var requested);
            *output = requested;
            return result;
        }
        catch (Exception)
        {
            *output = 0;
            return unchecked((int)0x80004005);
        }
        finally
        {
            ComAbi.Release(unknown);
        }
    }
}
