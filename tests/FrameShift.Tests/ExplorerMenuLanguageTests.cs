using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FrameShift.Core.Localization;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Microsoft.Win32;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class ExplorerMenuLanguageTests
{
    public static IEnumerable<object[]> Languages => AppLanguages.Supported.Select(language => new object[] { language.Code });

    [Theory]
    [MemberData(nameof(Languages))]
    public void InstalledMenus_ChangeOnlyCaptions_ForAllLanguages(string language)
    {
        using var fixture = new MenuFixture();
        fixture.Add(@".mp4\shell\FrameShift\shell\convert_video");
        fixture.Add(@".mp4\shell\FrameShiftAI\shell\create_subtitles_video");
        fixture.Add(@".pdf\shell\FrameShiftAI\shell\extract_text");
        var specific = @".mp4\shell\FrameShift\shell\extract_specific_frames";
        using (var parent = fixture.Root.CreateSubKey(specific))
        {
            parent.SetValue("MUIVerb", "Original caption");
            parent.SetValue("ExtendedSubCommandsKey", @"SystemFileAssociations\" + specific);
        }
        foreach (var child in new[] { "first_frame", "last_frame", "keyframes" })
            fixture.Add(specific + @"\shell\" + child);
        // Unknown actions, a different installation and missing captions are untouched.
        fixture.Add(@".mp4\shell\FrameShift\shell\future_action");
        fixture.Add(@".mkv\shell\FrameShift\shell\convert_video", @"C:\Other FrameShift\FrameShift.exe");
        fixture.Add(@".mp3\shell\FrameShift\shell\convert_audio");
        using (var missingCaption = fixture.Root.OpenSubKey(@".mp3\shell\FrameShift\shell\convert_audio", true))
            missingCaption!.DeleteValue("MUIVerb");
        var before = Snapshot(fixture.Root);
        var previousCulture = CultureInfo.CurrentUICulture;
        var changes = ExplorerMenuLanguage.FindCaptionChanges(fixture.Root, MenuFixture.Executable, language);
        Assert.Equal(7, changes.Count);
        ExplorerMenuLanguage.ApplyCaptionChanges(fixture.Root, changes);
        Assert.Equal(previousCulture, CultureInfo.CurrentUICulture);
        Assert.Empty(ExplorerMenuLanguage.FindCaptionChanges(fixture.Root, MenuFixture.Executable, language));
        var after = Snapshot(fixture.Root);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, value) in before)
        {
            var change = changes.SingleOrDefault(item => path == item.KeyPath + "|MUIVerb");
            Assert.Equal(change is null ? value : "String:" + change.Caption, after[path]);
        }
        using var converted = fixture.Root.OpenSubKey(@".mp4\shell\FrameShift\shell\convert_video");
        Assert.Equal(L.Get("ExplorerMenu_convert_video", CultureInfo.GetCultureInfo(language)), converted!.GetValue("MUIVerb"));
    }

    [Fact]
    public void RemovedMenu_IsNotRecreated_AndPreflightAvoidsPartialWrites()
    {
        using var fixture = new MenuFixture();
        var first = @".mp4\shell\FrameShift\shell\convert_video";
        var second = @".mp4\shell\FrameShift\shell\cut_video";
        fixture.Add(first);
        fixture.Add(second);
        var changes = ExplorerMenuLanguage.FindCaptionChanges(fixture.Root, MenuFixture.Executable, "ru");
        Assert.Equal(2, changes.Count);
        fixture.Root.DeleteSubKeyTree(second);
        Assert.Throws<IOException>(() => ExplorerMenuLanguage.ApplyCaptionChanges(fixture.Root, changes));
        using var remaining = fixture.Root.OpenSubKey(first);
        Assert.Equal("Original caption", remaining!.GetValue("MUIVerb"));
        Assert.Null(fixture.Root.OpenSubKey(second));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryMenuCaption_MatchesTheInstallerTranslation(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "installer", "FrameShift.iss")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(directory.FullName, "installer", "FrameShift.iss"));
        var installerName = language switch
        {
            "en" => "english", "zh-CN" => "chinesesimp", _ => AppLanguages.Resolve(language).Aliases.Single()
        };
        var expected = new Dictionary<string, string>();
        foreach (Match match in Regex.Matches(script,
            @"Apply(Action|AIAction)MenuList\(\s*(?:'[^']*'|[^,'])+,\s*'([^']+)',\s*CustomMessage\('([^']+)'\)"))
            expected[(match.Groups[1].Value == "AIAction" ? "FrameShiftAI" : "FrameShift") + @"\shell\" + match.Groups[2].Value] = match.Groups[3].Value;
        expected[@"FrameShift\shell\join_videos"] = "Text_a4500def3d";
        expected[@"FrameShift\shell\extract_all_frames"] = "Text_c74676dfc9";
        expected[@"FrameShift\shell\extract_specific_frames"] = "Text_722f989b80";
        foreach (Match match in Regex.Matches(script, @"ConfigureSpecificExtractFrameMenuForHive\(Hive, SpecificKeyPath, '([^']+)', CustomMessage\('([^']+)'\)"))
            expected[@"FrameShift\shell\extract_specific_frames\shell\" + match.Groups[1].Value] = match.Groups[2].Value;
        Assert.Equal(expected.Keys.Order(), ExplorerMenuLanguage.MenuResources.Keys.Order());
        foreach (var (path, messageKey) in expected)
        {
            var value = Regex.Match(script, @"(?m)^" + installerName + @"\." + messageKey + @"=(.*)\r?$").Groups[1].Value.TrimEnd('\r');
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.Equal(value, L.Get(ExplorerMenuLanguage.MenuResources[path], CultureInfo.GetCultureInfo(language)));
        }
        var installedExtensions = Regex.Matches(script, @"(?:Video|Audio|Image|PdfImage)Extensions = '([^']+)'" )
            .SelectMany(match => match.Groups[1].Value.Split(',')).Append(".pdf").Distinct().Order();
        Assert.Equal(installedExtensions, ExplorerMenuLanguage.Extensions.Order());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Elevation_UsesOnlyTheFixedHelperCommandAndRegisteredLanguage(string language)
    {
        var start = ExplorerMenuLanguage.CreateElevatedStartInfo(language);
        Assert.True(start.UseShellExecute);
        Assert.Equal("runas", start.Verb);
        Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, start.WindowStyle);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "FrameShift.exe"), start.FileName);
        Assert.Equal(new[] { ExplorerMenuLanguage.UpdateCommand, language }, start.ArgumentList);
        Assert.True(ExplorerMenuLanguage.TryParseUpdateCommand(start.ArgumentList.ToArray(), out var parsed));
        Assert.Equal(language, parsed);
    }

    [Fact]
    public void Helper_RejectsUnknownLanguagesAndAdditionalArguments_WithoutTouchingMenus()
    {
        foreach (var args in new[]
        {
            new[] { ExplorerMenuLanguage.UpdateCommand },
            new[] { ExplorerMenuLanguage.UpdateCommand, "unknown" },
            new[] { ExplorerMenuLanguage.UpdateCommand, "russian" },
            new[] { ExplorerMenuLanguage.UpdateCommand, "ru", @"C:\untrusted.json" },
            new[] { ExplorerMenuLanguage.UpdateCommand, @"ru\..\other" }
        })
        {
            Assert.False(ExplorerMenuLanguage.TryParseUpdateCommand(args, out _));
            Assert.True(ExplorerMenuLanguage.TryRunUpdateCommand(args, out var code));
            Assert.Equal(2, code);
        }
        Assert.False(ExplorerMenuLanguage.TryRunUpdateCommand(["--action", "convert-video"], out _));
    }

    [Theory]
    [InlineData((int)ExplorerMenuSyncResult.Updated, "Settings_ExplorerLanguageUpdated")]
    [InlineData((int)ExplorerMenuSyncResult.Cancelled, "Settings_ExplorerLanguageCancelled")]
    [InlineData((int)ExplorerMenuSyncResult.Failed, "Settings_ExplorerLanguageFailed")]
    public void Settings_SavesPreference_AndReportsTheActualMenuOutcome(int outcome, string statusKey)
    {
        StaTest.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FrameShift menus été 中文 русский " + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Path.Combine(directory, "ui-settings.json");
                new FrameShiftUiSettings { Language = "en", Theme = "Dark", OcrModel = "tiny" }.Save(path);
                var calls = new List<string>();
                using var form = new SettingsForm(path, language =>
                {
                    Assert.Equal(language, FrameShiftUiSettings.Load(path).Language);
                    calls.Add(language);
                    return Task.FromResult((ExplorerMenuSyncResult)outcome);
                });
                var selector = Descendants(form).OfType<ComboBox>().Single(control => control.Name == "languagePreference");
                Assert.Empty(calls); // Opening Settings must not prompt for UAC.
                selector.SelectedItem = AppLanguages.Resolve("ru");
                Assert.Equal(new[] { "ru" }, calls);
                Assert.Equal("ru", FrameShiftUiSettings.Load(path).Language);
                Assert.Equal("Dark", FrameShiftUiSettings.Load(path).Theme);
                Assert.Equal("tiny", FrameShiftUiSettings.Load(path).OcrModel);
                Assert.True(selector.Enabled);
                Assert.Equal(L.Get(statusKey), Descendants(form).OfType<Label>().Single(label => label.Name == "explorerLanguageStatus").Text);
                selector.SelectedItem = AppLanguages.Default;
                Assert.Equal(new[] { "ru", "en" }, calls);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Settings_WaitsWithoutBlocking_AndFitsTranslatedFailureMessages(string language)
    {
        StaTest.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            var directory = Path.Combine(Path.GetTempPath(), "FrameShift menu pending " + Guid.NewGuid().ToString("N"));
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var completion = new TaskCompletionSource<ExplorerMenuSyncResult>();
                var path = Path.Combine(directory, "ui-settings.json");
                new FrameShiftUiSettings { Language = "en" }.Save(path);
                using var form = new SettingsForm(path, _ => completion.Task);
                _ = form.Handle;
                var controls = Descendants(form).ToArray();
                var selector = controls.OfType<ComboBox>().Single(control => control.Name == "languagePreference");
                var close = controls.OfType<Button>().Single(button => button.DialogResult == DialogResult.Cancel);
                var hint = controls.OfType<Label>().Single(label => label.Name == "explorerLanguageStatus");
                selector.SelectedItem = AppLanguages.Resolve("ru");
                Assert.False(selector.Enabled);
                Assert.False(close.Enabled);
                Assert.Equal(L.Get("Settings_ExplorerLanguageUpdating"), hint.Text);
                completion.SetException(new IOException("Simulated menu update failure"));
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!selector.Enabled && timer.Elapsed < TimeSpan.FromSeconds(5))
                    Application.DoEvents();
                Assert.True(selector.Enabled);
                Assert.True(close.Enabled);
                Assert.Equal("ru", FrameShiftUiSettings.Load(path).Language);
                Assert.Equal(L.Get("Settings_ExplorerLanguageFailed"), hint.Text);
                FrameShiftDialogLayout.FitInitialHeight(form, form.Controls.OfType<TableLayoutPanel>().Single(), new System.Drawing.Size(1600, 1000));
                Assert.True(hint.Bottom <= hint.Parent!.ClientSize.Height);
                Assert.False(form.Visible);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Settings_DoesNotSynchronizeMenus_WhenSavingLanguageFails_AndAllowsRetry(string language)
    {
        StaTest.Run(() =>
        {
            var previousCulture = CultureInfo.CurrentUICulture;
            var directory = Path.Combine(Path.GetTempPath(), "FrameShift menu save été 中文 русский " + Guid.NewGuid().ToString("N"));
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var path = Path.Combine(directory, "ui-settings.json");
                var savedLanguage = language == "en" ? "zh-CN" : "en";
                new FrameShiftUiSettings
                {
                    Language = savedLanguage, Theme = "Dark", OcrModel = "tiny", OcrFormat = "txt",
                    OcrParagraphs = true, OcrDpi = 200, JoinVideosSortOrder = "Natural"
                }.Save(path);
                var original = File.ReadAllText(path);
                var calls = new List<string>();
                using var form = new SettingsForm(path, code =>
                {
                    calls.Add(code);
                    Assert.Equal(code, FrameShiftUiSettings.Load(path).Language);
                    return Task.FromResult(ExplorerMenuSyncResult.Updated);
                });
                _ = form.Handle;
                var controls = Descendants(form).ToArray();
                var selector = controls.OfType<ComboBox>().Single(control => control.Name == "languagePreference");
                var close = controls.OfType<Button>().Single(button => button.DialogResult == DialogResult.Cancel);
                var hint = controls.OfType<Label>().Single(label => label.Name == "explorerLanguageStatus");
                using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    selector.SelectedItem = AppLanguages.Resolve(language);
                    Assert.Empty(calls); // No menu update or UAC request without a saved preference.
                    Assert.Equal(original, File.ReadAllText(path));
                    Assert.Equal(savedLanguage, Assert.IsType<AppLanguage>(selector.SelectedItem).Code);
                    Assert.Equal(L.Get("Settings_LanguageSaveFailed"), hint.Text);
                    Assert.True(selector.Enabled);
                    Assert.True(close.Enabled);
                    Assert.True(hint.Bottom <= hint.Parent!.ClientSize.Height);
                }
                // The failed selection was restored, so choosing the same language
                // after unlocking the file really retries the operation.
                selector.SelectedItem = AppLanguages.Resolve(language);
                Assert.Equal(new[] { language }, calls);
                var saved = FrameShiftUiSettings.Load(path);
                Assert.Equal(language, saved.Language);
                Assert.Equal("Dark", saved.Theme);
                Assert.Equal("tiny", saved.OcrModel);
                Assert.Equal("txt", saved.OcrFormat);
                Assert.True(saved.OcrParagraphs);
                Assert.Equal(200, saved.OcrDpi);
                Assert.Equal("Natural", saved.JoinVideosSortOrder);
                Assert.Equal(L.Get("Settings_ExplorerLanguageUpdated"), hint.Text);
                Assert.Equal(language, CultureInfo.CurrentUICulture.Name);
                Assert.False(form.Visible);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousCulture;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void UiSettings_TrySaveReportsReadOnlyFiles_WhileSaveRemainsBestEffort()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrameShift read-only settings " + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ui-settings.json");
        try
        {
            var settings = new FrameShiftUiSettings { Language = "en", Theme = "Dark" };
            Assert.True(settings.TrySave(path));
            var original = File.ReadAllText(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            settings.Language = "ru";
            Assert.False(settings.TrySave(path));
            settings.Save(path); // Existing callers retain their non-throwing API.
            Assert.Equal(original, File.ReadAllText(path));
            File.SetAttributes(path, FileAttributes.Normal);
            Assert.True(settings.TrySave(path));
            Assert.Equal("ru", FrameShiftUiSettings.Load(path).Language);
            Assert.Equal("Dark", FrameShiftUiSettings.Load(path).Theme);
        }
        finally
        {
            if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void NativeDescriptions_UpdateExistingFlatCaptions_WithoutChangingCommands(string language)
    {
        using var fixture = new MenuFixture();
        foreach (var (leaf, resource) in ExplorerMenuLanguage.FlatResources)
        {
            var path = @".mp4\shell\FrameShift\shell\extract_specific_frames\shell\" + leaf;
            fixture.Add(path);
            using var key = fixture.Root.OpenSubKey(path, true);
            key!.SetValue("FlatMUIVerb", "Original flat caption");
        }
        var before = Snapshot(fixture.Root);
        var changes = ExplorerMenuLanguage.FindCaptionChanges(fixture.Root, MenuFixture.Executable, language);
        Assert.Equal(6, changes.Count);
        ExplorerMenuLanguage.ApplyCaptionChanges(fixture.Root, changes);
        var after = Snapshot(fixture.Root);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, value) in before)
        {
            var change = changes.SingleOrDefault(item => path == item.KeyPath + "|" + item.ValueName);
            Assert.Equal(change is null ? value : "String:" + change.Caption, after[path]);
        }
        Assert.Empty(ExplorerMenuLanguage.FindCaptionChanges(fixture.Root, MenuFixture.Executable, language));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void FlatExtractionCaptions_MatchInstallerAndKeepPublishedTranslations(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "installer", "FrameShift.iss"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(directory.FullName, "installer", "FrameShift.iss"));
        var installerLanguage = language switch { "en" => "english", "zh-CN" => "chinesesimp", _ => AppLanguages.Resolve(language).Aliases.Single() };
        foreach (var (resource, message) in new[] { ("extract_first_frame", "ShellExtractFirst"), ("extract_last_frame", "ShellExtractLast"), ("extract_keyframes", "ShellExtractKeys") })
        {
            var expected = Regex.Match(script, @"(?m)^" + installerLanguage + @"\." + message + @"=(.*)\r?$").Groups[1].Value.TrimEnd('\r');
            Assert.False(string.IsNullOrWhiteSpace(expected));
            Assert.Equal(expected, L.Get("ExplorerMenu_" + resource, CultureInfo.GetCultureInfo(language)));
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Dictionary<string, string> Snapshot(RegistryKey root, string path = "")
    {
        var values = root.GetValueNames().ToDictionary(name => path + "|" + name,
            name => root.GetValueKind(name) + ":" + root.GetValue(name));
        foreach (var child in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(child);
            foreach (var pair in Snapshot(key!, string.IsNullOrEmpty(path) ? child : path + @"\" + child))
                values.Add(pair.Key, pair.Value);
        }
        return values;
    }

    private sealed class MenuFixture : IDisposable
    {
        internal const string Executable = @"C:\Essais été 中文 русский\FrameShift.exe";
        private readonly string _path = @"Software\FrameShift.Tests\ExplorerMenuLanguage\" + Guid.NewGuid().ToString("N");
        internal RegistryKey Root { get; }
        internal MenuFixture() => Root = Registry.CurrentUser.CreateSubKey(_path);

        internal void Add(string path, string executable = Executable)
        {
            using var key = Root.CreateSubKey(path);
            key.SetValue("MUIVerb", "Original caption");
            key.SetValue("Icon", @"C:\Icons\été.ico");
            key.SetValue("MultiSelectModel", "Player");
            using var command = key.CreateSubKey("command");
            command.SetValue(null, '"' + executable + "\" --action convert-video \"%1\"");
        }

        public void Dispose()
        {
            Root.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
        }
    }
}
