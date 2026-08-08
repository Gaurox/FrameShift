using System;
using System.IO;
using System.Text.Json;
using FrameShift.Core.Logging;

namespace FrameShift.Core.AI;

/// <summary>
/// User preferences persisted in %LOCALAPPDATA%\FrameShift\config\settings.json.
/// Load() is tolerant: missing/corrupt file → defaults, never throws.
/// </summary>
public sealed class AiModelSettings
{
    private static readonly string DefaultConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift", "config");

    internal const string ConfigDirectoryOverrideEnvironmentVariable = "FRAMESHIFT_CONFIG_DIRECTORY";

    public static string ConfigFilePath => Path.Combine(GetConfigDirectory(), "settings.json");

    private static readonly string DefaultModelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameShift", "AI", "Models");

    internal static string DefaultModelsDirectoryPath => DefaultModelsDirectory;

    // Null/empty means "use default"
    public string? ModelsDirectory { get; set; }

    public static AiModelSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new AiModelSettings();

            var json = File.ReadAllText(ConfigFilePath);
            var settings = JsonSerializer.Deserialize<AiModelSettings>(json);
            return settings ?? new AiModelSettings();
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"AiModelSettings.Load: failed to read settings, using defaults. {ex.Message}");
            return new AiModelSettings();
        }
    }

    public void Save()
    {
        try
        {
            var configFilePath = ConfigFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFilePath, json);
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"AiModelSettings.Save: failed to write settings. {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the effective models directory. Validates the custom path:
    /// if absent, empty, or not writable → falls back to the default path.
    /// </summary>
    public string GetEffectiveModelsDirectory()
    {
        if (!string.IsNullOrWhiteSpace(ModelsDirectory))
        {
            if (AiModelDirectorySafety.TryNormalizeCustomDirectory(ModelsDirectory, out var candidate) &&
                IsDirectoryUsable(candidate))
            {
                return candidate;
            }

            AppLogger.LogStatic(
                $"AiModelSettings: custom ModelsDirectory is unsafe or not usable, falling back to default.");
        }

        return DefaultModelsDirectory;
    }

    private static bool IsDirectoryUsable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            // Quick write test
            var probe = Path.Combine(path, ".writetest");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetConfigDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(ConfigDirectoryOverrideEnvironmentVariable);
        return string.IsNullOrWhiteSpace(overrideDirectory)
            ? DefaultConfigDirectory
            : overrideDirectory;
    }
}
