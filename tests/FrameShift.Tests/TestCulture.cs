using System.Globalization;
using System.Runtime.CompilerServices;

namespace FrameShift.Tests;

internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Most behavioral assertions use English messages. Keep them independent
        // of the host Windows language; localization tests select cultures explicitly.
        // Leave numeric culture unchanged so comma-decimal cases remain exercised.
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }
}
