using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using FrameShift.Core.Logging;
using Microsoft.Win32;

namespace FrameShift.Windows.Helpers;

internal enum ExplorerMenuSyncResult { Updated, Cancelled, Failed }

internal sealed record ExplorerMenuCaptionChange(string KeyPath, string Caption, string ValueName = "MUIVerb");

// The installer owns menu registration. Settings only replaces existing captions,
// never commands, icons, component selections or file associations.
internal static class ExplorerMenuLanguage
{
    internal const string UpdateCommand = "--update-explorer-menu-language";
    private const string AssociationsPath = @"Software\Classes\SystemFileAssociations";
    internal const string NativeMenusPath = @"Software\FrameShift\ShellMenus";
    internal static readonly IReadOnlyDictionary<string, string> FlatResources = new Dictionary<string, string>
    {
        ["first_frame"] = "ExplorerMenu_extract_first_frame",
        ["last_frame"] = "ExplorerMenu_extract_last_frame",
        ["keyframes"] = "ExplorerMenu_extract_keyframes"
    };
    internal static readonly string[] Extensions =
        ".mp4,.mkv,.avi,.mov,.webm,.m4v,.mp3,.wav,.wave,.flac,.m4a,.ogg,.aac,.wma,.png,.jpg,.jpeg,.webp,.bmp,.pdf".Split(',');

    internal static readonly IReadOnlyDictionary<string, string> MenuResources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"FrameShift\shell\convert_video"] = "ExplorerMenu_convert_video",
            [@"FrameShift\shell\convert_audio"] = "ExplorerMenu_convert_audio",
            [@"FrameShift\shell\cut_audio"] = "ExplorerMenu_cut_audio",
            [@"FrameShift\shell\remove_audio"] = "ExplorerMenu_remove_audio",
            [@"FrameShift\shell\reverse_audio"] = "ExplorerMenu_reverse_audio",
            [@"FrameShift\shell\compress_audio"] = "ExplorerMenu_compress_audio",
            [@"FrameShift\shell\change_pitch"] = "ExplorerMenu_change_pitch",
            [@"FrameShift\shell\change_audio_speed"] = "ExplorerMenu_change_audio_speed",
            [@"FrameShift\shell\change_video_speed"] = "ExplorerMenu_change_video_speed",
            [@"FrameShift\shell\create_gif"] = "ExplorerMenu_create_gif",
            [@"FrameShift\shell\add_subtitles_video"] = "ExplorerMenu_add_subtitles_video",
            [@"FrameShift\shell\extract_audio"] = "ExplorerMenu_extract_audio",
            [@"FrameShift\shell\cut_video"] = "ExplorerMenu_cut_video",
            [@"FrameShift\shell\crop_video"] = "ExplorerMenu_crop_video",
            [@"FrameShift\shell\resize_video"] = "ExplorerMenu_resize_video",
            [@"FrameShift\shell\compress_video"] = "ExplorerMenu_compress_video",
            [@"FrameShift\shell\interpolate_video"] = "ExplorerMenu_interpolate_video",
            [@"FrameShift\shell\compress_image"] = "ExplorerMenu_compress_image",
            [@"FrameShift\shell\crop_image"] = "ExplorerMenu_crop_image",
            [@"FrameShift\shell\resize_image"] = "ExplorerMenu_resize_image",
            [@"FrameShift\shell\convert_image"] = "ExplorerMenu_convert_image",
            [@"FrameShift\shell\convert_icon"] = "ExplorerMenu_convert_icon",
            [@"FrameShift\shell\image_to_pdf"] = "ExplorerMenu_image_to_pdf",
            [@"FrameShift\shell\rotate_flip_image"] = "ExplorerMenu_rotate_flip_image",
            [@"FrameShift\shell\rotate_flip_video"] = "ExplorerMenu_rotate_flip_video",
            [@"FrameShift\shell\media_info"] = "ExplorerMenu_media_info",
            [@"FrameShiftAI\shell\remove_background_fast"] = "ExplorerMenu_remove_background_fast",
            [@"FrameShiftAI\shell\remove_background_high_resolution"] = "ExplorerMenu_remove_background_high_resolution",
            [@"FrameShiftAI\shell\remove_background_high_resolution_general"] = "ExplorerMenu_remove_background_high_resolution_general",
            [@"FrameShiftAI\shell\remove_background_bria_balanced"] = "ExplorerMenu_remove_background_bria_balanced",
            [@"FrameShiftAI\shell\remove_background_bria_high_quality"] = "ExplorerMenu_remove_background_bria_high_quality",
            [@"FrameShiftAI\shell\remove_noise"] = "ExplorerMenu_remove_noise",
            [@"FrameShiftAI\shell\remove_noise_video"] = "ExplorerMenu_remove_noise_video",
            [@"FrameShiftAI\shell\separate_audio"] = "ExplorerMenu_separate_audio",
            [@"FrameShiftAI\shell\interpolate_video_rife"] = "ExplorerMenu_interpolate_video_rife",
            [@"FrameShiftAI\shell\remove_object"] = "ExplorerMenu_remove_object",
            [@"FrameShiftAI\shell\upscale_image"] = "ExplorerMenu_upscale_image",
            [@"FrameShiftAI\shell\upscale_video"] = "ExplorerMenu_upscale_video",
            [@"FrameShiftAI\shell\create_subtitles_audio"] = "ExplorerMenu_create_subtitles_audio",
            [@"FrameShiftAI\shell\create_subtitles_video"] = "ExplorerMenu_create_subtitles_video",
            [@"FrameShiftAI\shell\extract_text"] = "ExplorerMenu_extract_text",
            [@"FrameShift\shell\join_videos"] = "ExplorerMenu_join_videos",
            [@"FrameShift\shell\extract_all_frames"] = "ExplorerMenu_extract_all_frames",
            [@"FrameShift\shell\extract_specific_frames"] = "ExplorerMenu_extract_specific_frames",
            [@"FrameShift\shell\extract_specific_frames\shell\first_frame"] = "ExplorerMenu_first_frame",
            [@"FrameShift\shell\extract_specific_frames\shell\last_frame"] = "ExplorerMenu_last_frame",
            [@"FrameShift\shell\extract_specific_frames\shell\keyframes"] = "ExplorerMenu_keyframes",
        };

    private static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "FrameShift.exe");

    internal static async Task<ExplorerMenuSyncResult> SynchronizeAsync(string language)
    {
        try
        {
            var code = AppLanguages.Resolve(language).Code;
            try
            {
                await Task.Run(() => UpdateMenus(RegistryHive.LocalMachine, code));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
            {
                // Only the fixed command and a registered language cross the UAC
                // boundary. The child derives registry and executable paths itself.
                var start = CreateElevatedStartInfo(code);
                using var helper = Process.Start(start);
                if (helper is null) return ExplorerMenuSyncResult.Failed;
                await helper.WaitForExitAsync();
                if (helper.ExitCode != 0) return ExplorerMenuSyncResult.Failed;
            }

            // Do this in the original account: UAC can use another administrator's
            // credentials, whose HKCU must never receive this user's preferences.
            await Task.Run(() => UpdateMenus(RegistryHive.CurrentUser, code));
            NotifyExplorer();
            return ExplorerMenuSyncResult.Updated;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ExplorerMenuSyncResult.Cancelled;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"Explorer menu language update failed: {ex}");
            return ExplorerMenuSyncResult.Failed;
        }
    }

    internal static ProcessStartInfo CreateElevatedStartInfo(string language)
    {
        var start = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(UpdateCommand);
        start.ArgumentList.Add(language);
        return start;
    }

    internal static bool TryParseUpdateCommand(string[] args, out string language)
    {
        language = string.Empty;
        if (args.Length != 2 || args[0] != UpdateCommand) return false;
        var registered = AppLanguages.Supported.FirstOrDefault(item => item.Code == args[1]);
        if (registered is null) return false;
        language = registered.Code;
        return true;
    }

    internal static bool TryRunUpdateCommand(string[] args, out int exitCode)
    {
        exitCode = 2;
        if (args.Length == 0 || args[0] != UpdateCommand) return false;
        if (!TryParseUpdateCommand(args, out var language)) return true;
        try
        {
            UpdateMenus(RegistryHive.LocalMachine, language);
            NotifyExplorer();
            exitCode = 0;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"Elevated Explorer menu language update failed: {ex}");
            exitCode = 1;
        }
        return true;
    }

    private static void UpdateMenus(RegistryHive hive, string language)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var associations = root.OpenSubKey(AssociationsPath);
        using var nativeMenus = root.OpenSubKey(NativeMenusPath);
        var legacyChanges = associations is null ? [] : FindCaptionChanges(associations, ExecutablePath, language);
        var nativeChanges = nativeMenus is null ? [] : FindCaptionChanges(nativeMenus, ExecutablePath, language);
        // Preflight both stores before the first write, so elevation cannot leave half an update.
        using var legacyAccess = associations is null ? null : OpenWritableMenus(associations, legacyChanges);
        using var nativeAccess = nativeMenus is null ? null : OpenWritableMenus(nativeMenus, nativeChanges);
        legacyAccess?.Apply(legacyChanges);
        nativeAccess?.Apply(nativeChanges);
    }

    internal static List<ExplorerMenuCaptionChange> FindCaptionChanges(
        RegistryKey associations, string executablePath, string language)
    {
        var changes = new List<ExplorerMenuCaptionChange>();
        var culture = CultureInfo.GetCultureInfo(AppLanguages.Resolve(language).Code);
        foreach (var extension in Extensions)
        foreach (var (menuPath, resourceKey) in MenuResources)
        {
            var keyPath = extension + @"\shell\" + menuPath;
            using var menu = associations.OpenSubKey(keyPath);
            if (menu is null || menu.GetValue("MUIVerb") is not string caption ||
                !BelongsToExecutable(menu, executablePath)) continue;
            var translated = L.Get(resourceKey, culture);
            if (caption != translated) changes.Add(new(keyPath, translated));
            var leaf = menuPath[(menuPath.LastIndexOf('\\') + 1)..];
            if (FlatResources.TryGetValue(leaf, out var flatResource) && menu.GetValue("FlatMUIVerb") is string flatCaption)
            {
                var flatTranslated = L.Get(flatResource, culture);
                if (flatCaption != flatTranslated) changes.Add(new(keyPath, flatTranslated, "FlatMUIVerb"));
            }
        }
        return changes;
    }

    private static bool BelongsToExecutable(RegistryKey menu, string executablePath)
    {
        using var command = menu.OpenSubKey("command");
        if (command?.GetValue(null) is string value)
            return value.StartsWith('"' + executablePath + "\" --action ", StringComparison.OrdinalIgnoreCase);

        // The "Extract specific frames" parent has no command of its own.
        // Its known children establish that this menu belongs to this install.
        foreach (var child in new[] { "first_frame", "last_frame", "keyframes" })
        {
            using var childCommand = menu.OpenSubKey(@"shell\" + child + @"\command");
            if (childCommand?.GetValue(null) is string childValue &&
                childValue.StartsWith('"' + executablePath + "\" --action ", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static void ApplyCaptionChanges(RegistryKey associations, IReadOnlyList<ExplorerMenuCaptionChange> changes)
    {
        using var access = OpenWritableMenus(associations, changes);
        access.Apply(changes);
    }

    private static WritableMenus OpenWritableMenus(RegistryKey associations, IReadOnlyList<ExplorerMenuCaptionChange> changes)
    {
        var access = new WritableMenus();
        try
        {
            // Open every existing key before the first write. Access denied then
            // requests elevation without first applying part of the translation.
            foreach (var change in changes)
                access.Keys.Add(associations.OpenSubKey(change.KeyPath, writable: true)
                    ?? throw new IOException("An Explorer menu was removed during its language update."));
            return access;
        }
        catch
        {
            access.Dispose();
            throw;
        }
    }

    private sealed class WritableMenus : IDisposable
    {
        internal List<RegistryKey> Keys { get; } = [];
        internal void Apply(IReadOnlyList<ExplorerMenuCaptionChange> changes)
        {
            for (var index = 0; index < changes.Count; index++)
                Keys[index].SetValue(changes[index].ValueName, changes[index].Caption, RegistryValueKind.String);
        }
        public void Dispose() { foreach (var key in Keys) key.Dispose(); }
    }

    private static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
