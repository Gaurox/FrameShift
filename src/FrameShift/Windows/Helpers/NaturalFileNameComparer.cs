using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FrameShift.Windows.Helpers;

internal sealed class NaturalFileNameComparer : IComparer<string>
{
    public static NaturalFileNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (OperatingSystem.IsWindows())
        {
            return StrCmpLogicalW(left, right);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string first, string second);
}
