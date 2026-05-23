using System.Collections.Generic;
using System.Text.Json;
using FrameShift.Core.Helpers;

namespace FrameShift.Core.Actions;

public sealed class ImageToPdfSettings
{
    public const string DefaultPageFormat = "A4";

    public string PageFormat { get; set; } = DefaultPageFormat;

    public double CustomPageWidthCm { get; set; }

    public double CustomPageHeightCm { get; set; }

    public List<ImageToPdfItemSettings> Items { get; set; } = [];

    public string ToOptionPayload()
    {
        return JsonSerializer.Serialize(this);
    }

    public bool HasValidCustomPageSize()
    {
        if (!ImageToPdfGeometry.IsCustomPageFormat(PageFormat))
        {
            return true;
        }

        return CustomPageWidthCm > 0 && CustomPageHeightCm > 0;
    }

    public static string NormalizePageFormat(string? value)
    {
        return ImageToPdfGeometry.NormalizePageFormat(value);
    }

    public static bool TryFromOptions(
        IReadOnlyDictionary<string, string>? options,
        out ImageToPdfSettings? settings,
        out string? error)
    {
        settings = null;
        error = null;

        if (options is null ||
            !options.TryGetValue(ActionOptionKeys.ImageToPdfSettings, out var payload) ||
            string.IsNullOrWhiteSpace(payload))
        {
            error = MediaActionMessages.ImageToPdfSettingsMissing();
            return false;
        }

        try
        {
            settings = JsonSerializer.Deserialize<ImageToPdfSettings>(payload);
        }
        catch (JsonException)
        {
            error = MediaActionMessages.ImageToPdfSettingsInvalid();
            return false;
        }

        if (settings is null || settings.Items.Count == 0)
        {
            error = MediaActionMessages.ImageToPdfSettingsInvalid();
            return false;
        }

        settings.PageFormat = NormalizePageFormat(settings.PageFormat);
        if (!settings.HasValidCustomPageSize())
        {
            error = MediaActionMessages.ImageToPdfPageSizeInvalid();
            return false;
        }

        return true;
    }
}
