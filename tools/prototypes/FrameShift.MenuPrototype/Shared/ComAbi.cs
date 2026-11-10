namespace FrameShift.MenuPrototype;

internal static unsafe class ComAbi
{
    internal static readonly Guid ClassId = new("125E0C69-6109-4A6F-8DE4-8B72C32D2814");
    internal static readonly Guid UnknownId = new("00000000-0000-0000-C000-000000000046");
    internal static readonly Guid FactoryId = new("00000001-0000-0000-C000-000000000046");
    internal static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");
    internal const int NoInterface = unchecked((int)0x80004002);
    internal const int InvalidArgument = unchecked((int)0x80070057);
    internal const int NotImplemented = unchecked((int)0x80004001);
    internal const int InsufficientBuffer = unchecked((int)0x8007007A);
    internal const int NoAggregation = unchecked((int)0x80040110);
    internal const int ClassUnavailable = unchecked((int)0x80040111);

    internal static int QueryInterface(nint instance, Guid id, out nint result)
    {
        result = 0;
        fixed (nint* output = &result)
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)instance)[0])(instance, &id, output);
    }

    internal static uint Release(nint instance) => instance == 0 ? 0
        : ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
}
