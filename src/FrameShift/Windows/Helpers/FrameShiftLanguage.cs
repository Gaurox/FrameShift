namespace FrameShift.Windows.Helpers;

internal static class FrameShiftLanguage
{
    internal static void Initialize()
    {
        var language = FrameShiftUiSettings.Load().Language;
        if (string.IsNullOrWhiteSpace(language))
        {
            try { language = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "language.txt")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        L.Initialize(language);
    }
}
