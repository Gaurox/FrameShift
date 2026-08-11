using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrameShift.Core.Logging;

namespace FrameShift.Windows.Helpers;

/// <summary>
/// UI preferences persisted separately from AI model settings.
/// Loading and saving are best effort so a damaged preference never blocks startup.
/// </summary>
public sealed class FrameShiftUiSettings
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift",
        "config");

    public static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "ui-settings.json");

    public string? Theme { get; set; } = nameof(FrameShiftThemePreference.System);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JoinVideosSortOrder { get; set; }

    public FrameShiftThemePreference GetThemePreference()
    {
        return ParseThemePreference(Theme);
    }

    public static FrameShiftUiSettings Load()
    {
        return Load(ConfigFilePath);
    }

    internal static FrameShiftUiSettings Load(string configFilePath)
    {
        try
        {
            if (!File.Exists(configFilePath))
            {
                return new FrameShiftUiSettings();
            }

            var json = File.ReadAllText(configFilePath);
            return JsonSerializer.Deserialize<FrameShiftUiSettings>(json) ?? new FrameShiftUiSettings();
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"FrameShiftUiSettings.Load: failed to read UI settings, using System. {ex.Message}");
            return new FrameShiftUiSettings();
        }
    }

    public void Save()
    {
        Save(ConfigFilePath);
    }

    internal void Save(string configFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(configFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The UI settings path must have a directory.");
            }

            Directory.CreateDirectory(directory);
            var settingsToSave = new FrameShiftUiSettings
            {
                Theme = GetThemePreference().ToString(),
                JoinVideosSortOrder = JoinVideosSortOrder
            };
            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"FrameShiftUiSettings.Save: failed to write UI settings. {ex.Message}");
        }
    }

    internal static FrameShiftThemePreference ParseThemePreference(string? value)
    {
        if (Enum.TryParse<FrameShiftThemePreference>(value, ignoreCase: true, out var preference) &&
            Enum.IsDefined(preference))
        {
            return preference;
        }

        return FrameShiftThemePreference.System;
    }
}
