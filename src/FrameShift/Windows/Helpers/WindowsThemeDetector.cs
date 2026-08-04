using Microsoft.Win32;

namespace FrameShift.Windows.Helpers;

internal static class WindowsThemeDetector
{
    private const string PersonalizeKeyPath = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

    public static bool UsesLightTheme()
    {
        try
        {
            var value = Registry.GetValue(PersonalizeKeyPath, AppsUseLightThemeValueName, defaultValue: null);
            return value is int intValue ? intValue != 0 : true;
        }
        catch
        {
            return true;
        }
    }
}
