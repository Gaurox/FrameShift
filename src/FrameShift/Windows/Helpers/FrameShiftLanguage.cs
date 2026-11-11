namespace FrameShift.Windows.Helpers;

internal static class FrameShiftLanguage
{
    internal const string InstallationFileName = "language-installation.txt";

    internal static void Initialize() => Initialize(FrameShiftUiSettings.ConfigFilePath, AppContext.BaseDirectory);

    internal static void Initialize(string settingsPath, string installationDirectory)
    {
        var settings = FrameShiftUiSettings.Load(settingsPath);
        var language = settings.Language;
        try
        {
            // The installer writes a new ID and language together on every run,
            // including reinstalls. Each user consumes that choice only once.
            var lines = File.ReadAllLines(Path.Combine(installationDirectory, InstallationFileName));
            if (lines.Length == 2 && Guid.TryParseExact(lines[0], "N", out var installationId) &&
                installationId != Guid.Empty && AppLanguages.Supported.Any(item => item.Code == lines[1]))
            {
                var id = installationId.ToString("N");
                if (!string.Equals(settings.AppliedLanguageInstallationId, id, StringComparison.OrdinalIgnoreCase))
                {
                    language = lines[1];
                    settings.Language = language;
                    settings.AppliedLanguageInstallationId = id;
                    // Save in the launching user's account, never the elevated
                    // installer's account. A failed save is retried next launch.
                    settings.TrySave(settingsPath);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        if (string.IsNullOrWhiteSpace(language))
        {
            try { language = File.ReadAllText(Path.Combine(installationDirectory, "language.txt")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        L.Initialize(language);
    }
}
