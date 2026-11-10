using System.Xml.Linq;

namespace FrameShift.ShellMenu;

internal static unsafe partial class Program
{
    // Independent expected action contracts; do not derive expectations from the production table.
    private sealed record ExpectedCommand(string Path, string Resource, string Action, string[] Extensions,
        bool Ai = false, bool Single = false, string? Model = null, string? Mode = null)
    {
        internal string Group => Ai ? "FrameShiftAI" : "FrameShift";
        internal string Verb => "frameshift." + Action + (Mode is null ? "" : "." + Mode) + (Model is null ? "" : "." + Model);
    }

    private static readonly string[] Videos = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v"];
    private static readonly string[] Audios = [".mp3", ".wav", ".wave", ".flac", ".m4a", ".ogg", ".aac", ".wma"];
    private static readonly string[] Images = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
    private static readonly ExpectedCommand[] UnifiedCommands =
    [
        .. Commands.Select(command => new ExpectedCommand(command.Path, command.Resource, command.Action, Videos,
            Single: command.Single, Mode: command.Mode)),
        new("media_info", "media_info", "media-info", [.. Audios, .. Images], Single: true),
        new("change_audio_speed", "change_audio_speed", "change-audio-speed", Audios, Single: true),
        new("change_pitch", "change_pitch", "change-pitch", Audios, Single: true),
        new("compress_audio", "compress_audio", "compress-audio", Audios),
        new("convert_audio", "convert_audio", "convert-audio", [".mp3", ".wav", ".wave", ".flac", ".m4a", ".ogg"]),
        new("cut_audio", "cut_audio", "cut-audio", Audios, Single: true),
        new("reverse_audio", "reverse_audio", "reverse-audio", Audios),
        new("compress_image", "compress_image", "compress-image", Images),
        new("convert_icon", "convert_icon", "convert-to-icon", Images, Single: true),
        new("convert_image", "convert_image", "convert-image", Images),
        new("crop_image", "crop_image", "crop-image", Images, Single: true),
        new("image_to_pdf", "image_to_pdf", "image-to-pdf", Images),
        new("resize_image", "resize_image", "resize-image", Images, Single: true),
        new("rotate_flip_image", "rotate_flip_image", "rotate-flip-image", Images, Single: true),
        new("create_subtitles_audio", "create_subtitles_audio", "create-subtitles-audio", Audios, Ai: true),
        new("create_subtitles_video", "create_subtitles_video", "create-subtitles-video", Videos, Ai: true),
        new("extract_text", "extract_text", "extract-text", [.. Images, ".pdf"], Ai: true),
        new("interpolate_video_rife", "interpolate_video_rife", "interpolate-video-rife", Videos, Ai: true, Single: true),
        .. new[] { "bria-balanced", "bria-high-quality", "fast", "high-resolution", "high-resolution-general" }
            .Select(model => new ExpectedCommand("remove_background_" + model.Replace('-', '_'),
                "remove_background_" + model.Replace('-', '_'), "remove-background", Images, Ai: true, Model: model)),
        new("remove_noise", "remove_noise", "remove-noise", [".wav", ".flac", ".mp3", ".ogg", ".m4a"], Ai: true),
        new("remove_noise_video", "remove_noise_video", "remove-noise-video", Videos, Ai: true),
        new("remove_object", "remove_object", "remove-object", Images, Ai: true, Single: true),
        new("separate_audio", "separate_audio", "separate-audio", [".wav", ".mp3", ".flac", ".m4a", ".ogg", ".aac", ".wma"], Ai: true),
        new("upscale_image", "upscale_image", "upscale-image", Images, Ai: true),
        new("upscale_video", "upscale_video", "upscale-video", Videos, Ai: true)
    ];

    private static void RunUnifiedMenus()
    {
        foreach (var language in new[] { "en", "zh-CN", "es", "pt-BR", "ru" })
            Run("Adjacent classic/AI roots, all media families and flat translations: " + language, () =>
            {
                var captions = PopulateUnified(language);
                foreach (var extension in Videos.Concat(Audios).Concat(Images).Append(".pdf"))
                {
                    var selected = FixtureFiles(extension, 1);
                    Initialize(selected);
                    var expected = UnifiedCommands.Where(command => command.Extensions.Contains(extension)).ToArray();
                    WithMenu(menu =>
                    {
                        NativeMenu.Insert(menu, 0, "Host before", 80);
                        NativeMenu.Insert(menu, 1, "Host after", 81);
                        Require(Query(menu, 1, 2000, 2099) == expected.Length, "Wrong command count for " + extension);
                        var groups = extension == ".pdf" ? new[] { "FrameShift AI" } : new[] { "FrameShift", "FrameShift AI" };
                        Require(NativeMenu.GetMenuItemCount(menu) == groups.Length + 2, "Root count changed.");
                        Require(NativeMenu.Read(menu, 0).Caption == "Host before"
                            && NativeMenu.Read(menu, (uint)groups.Length + 1).Caption == "Host after", "Host entries moved.");
                        var seen = new HashSet<string>();
                        uint offset = 0;
                        for (uint root = 0; root < groups.Length; root++)
                        {
                            var group = NativeMenu.Read(menu, root + 1);
                            Require(group.Caption == groups[root] && group.Submenu != 0, "Roots are not adjacent/in order.");
                            var ai = groups[root] == "FrameShift AI";
                            Require(NativeMenu.GetMenuItemCount(group.Submenu) == expected.Count(command => command.Ai == ai), "Missing group actions.");
                            for (uint index = 0; index < NativeMenu.GetMenuItemCount(group.Submenu); index++, offset++)
                            {
                                var item = NativeMenu.Read(group.Submenu, index);
                                var verb = ReadVerb(offset);
                                var command = expected.Single(command => command.Verb == verb);
                                Require(command.Ai == ai && seen.Add(verb), "Command in wrong group/duplicate.");
                                Require(item.Id == 2000 + offset && item.Submenu == 0 && item.Caption == captions[command.Resource], "Wrong ID/caption/flattening.");
                            }
                        }
                        Require(seen.Count == expected.Length, "Incomplete command coverage.");
                    });
                    Require(LastLaunch().Length == 0, "Opening a menu launched a process.");
                }
            });

        Run("Every unified action routes exact full selection and AI model arguments", () =>
        {
            PopulateUnified("en");
            foreach (var command in UnifiedCommands)
            {
                var paths = FixtureFiles(command.Extensions[0], command.Single ? 1 : 2);
                Initialize(paths);
                WithMenu(menu =>
                {
                    var count = Query(menu, 0, 100, 199);
                    Require(count > 0, "Missing family menu.");
                    var offset = Enumerable.Range(0, count).Single(index => ReadVerb((uint)index) == command.Verb);
                    var info = new Invocation { Size = 56, Verb = offset };
                    Check(Invoke(&info));
                });
                var expected = new List<string> { _executable, "--action", command.Action };
                if (command.Mode is not null) expected.AddRange(["--frame-mode", command.Mode]);
                if (command.Model is not null) expected.AddRange(["--rmbg-model", command.Model]);
                expected.AddRange(paths);
                Require(LastLaunch().SequenceEqual(expected), "Wrong launch for " + command.Verb);
            }
        });
        Run("AI-only/classic-only/core-only components and old video schema are respected", () =>
        {
            PopulateUnified("en"); Initialize(FixtureFiles(".png", 1));
            _root.DeleteSubKeyTree(@".png\shell\FrameShift", false);
            WithMenu(menu => Require(Query(menu, 0, 100, 199) == 8 && NativeMenu.GetMenuItemCount(menu) == 1
                && NativeMenu.Read(menu, 0).Caption == "FrameShift AI", "AI-only selection lost."));
            PopulateUnified("en");
            _root.DeleteSubKeyTree(@".png\shell\FrameShiftAI", false);
            WithMenu(menu => Require(Query(menu, 0, 100, 199) == 8 && NativeMenu.GetMenuItemCount(menu) == 1
                && NativeMenu.Read(menu, 0).Caption == "FrameShift", "Classic-only selection lost."));
            _root.DeleteSubKeyTree(".png", false);
            WithMenu(menu => Require(Query(menu, 0, 100, 199) == 0 && NativeMenu.GetMenuItemCount(menu) == 0, "Core-only menu appeared."));
            PopulateUnified("en"); _root.SetValue("SchemaVersion", 1);
            WithMenu(menu => Require(Query(menu, 0, 100, 199) == 0, "Old video schema accepted."));
        });
        Run("Both roots reject exhausted IDs and repeated lifetimes keep USER/GDI bounded", () =>
        {
            PopulateUnified("en"); Initialize(FixtureFiles(".png", 1));
            WithMenu(menu => Require(Query(menu, 0, 100, 114) == ComAbi.InsufficientBuffer
                && NativeMenu.GetMenuItemCount(menu) == 0, "Partial root pair after ID exhaustion."));
            WithMenu(menu => Check(Query(menu, 0, 100, 199)));
            var process = NativeMenu.GetCurrentProcess();
            var user = NativeMenu.GetGuiResources(process, 1); var gdi = NativeMenu.GetGuiResources(process, 0);
            for (var index = 0; index < 200; index++) WithMenu(menu => Require(Query(menu, 0, 100, 199) == 16, "Repeated unified menu failed."));
            for (var index = 0; index < 100; index++) Require(Query(-1, 0, 100, 199) < 0, "Invalid parent accepted.");
            Require(NativeMenu.GetGuiResources(process, 1) == user && NativeMenu.GetGuiResources(process, 0) == gdi, "Unified menu leaked USER/GDI objects.");
        });
    }

    private static Dictionary<string, string> PopulateUnified(string language)
    {
        Populate(language); // Resets the isolated fixture key and launch capture.
        var suffix = language == "en" ? "" : "." + language;
        var resources = XDocument.Load(Path.Combine(_sourceRoot, "src/FrameShift/Core/Localization/Strings" + suffix + ".resx"))
            .Root!.Elements("data").Where(item => ((string)item.Attribute("name")!).StartsWith("ExplorerMenu_", StringComparison.Ordinal))
            .ToDictionary(item => ((string)item.Attribute("name")!)["ExplorerMenu_".Length..], item => item.Element("value")!.Value);
        foreach (var command in UnifiedCommands)
        foreach (var extension in command.Extensions)
        {
            using var key = _root.CreateSubKey(extension + @"\shell\" + command.Group + @"\shell\" + command.Path);
            key.SetValue(command.Mode is not null and not "all" ? "FlatMUIVerb" : "MUIVerb", resources[command.Resource]);
            key.SetValue("Icon", Path.Combine(Path.GetDirectoryName(_executable)!, "Assets/Icons/app/app.ico"));
            using var commandKey = key.CreateSubKey("command");
            commandKey.SetValue("", '"' + _executable + "\" --action " + command.Action
                + (command.Mode is null ? "" : " --frame-mode " + command.Mode)
                + (command.Model is null ? "" : " --rmbg-model " + command.Model) + " \"%1\"");
        }
        return resources;
    }

    private static string[] FixtureFiles(string extension, int count)
    {
        var paths = Enumerable.Range(0, count).Select(index => Path.Combine(Path.GetDirectoryName(_files[0])!, "Média été " + index + extension)).ToArray();
        foreach (var path in paths) File.WriteAllBytes(path, []);
        return paths;
    }
}
