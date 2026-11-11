using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Localization;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class LocalizationTests
{
    public static IEnumerable<object[]> TranslatedLanguages => AppLanguages.Supported
        .Where(language => language.Code != AppLanguages.Default.Code)
        .Select(language => new object[] { language.Code });

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void RegisteredResources_AreComplete_AndRetainEveryFormatArgument(string language)
    {
        var manager = new ResourceManager("FrameShift.Core.Localization.Strings", typeof(MediaActionMessages).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var translatedResources = manager.GetResourceSet(CultureInfo.GetCultureInfo(language), true, false);
        Assert.NotNull(translatedResources);
        Assert.True(english.Cast<DictionaryEntry>().Count() > 1700);
        Assert.Equal(english.Cast<DictionaryEntry>().Count(), translatedResources.Cast<DictionaryEntry>().Count());
        foreach (DictionaryEntry entry in english)
        {
            var translated = translatedResources.GetString((string)entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(translated), (string)entry.Key);
            if (!Regex.IsMatch((string)entry.Value!, @"\{\d+[}:,]")) continue;
            var en = CompositeFormat.Parse((string)entry.Value!);
            var zh = CompositeFormat.Parse(translated!);
            Assert.True(en.MinimumArgumentCount == zh.MinimumArgumentCount, (string)entry.Key);
            // Interpolations may be reordered in a translation, but none may be lost or duplicated.
            var enArguments = Regex.Matches((string)entry.Value!, @"\{(\d+)[}:,]").Select(m => m.Groups[1].Value).Order().ToArray();
            var zhArguments = Regex.Matches(translated!, @"\{(\d+)[}:,]").Select(m => m.Groups[1].Value).Order().ToArray();
            Assert.Equal(enArguments, zhArguments);
            _ = string.Format(CultureInfo.InvariantCulture, zh, Enumerable.Repeat<object>(1, zh.MinimumArgumentCount).ToArray());
        }
    }

    [Theory]
    [InlineData("english", "en")]
    [InlineData("chinesesimp", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-Hans-CN", "zh-CN")]
    [InlineData(" zh_cn ", "zh-CN")]
    [InlineData(" CHINESESIMP ", "zh-CN")]
    [InlineData("en-GB", "en")]
    [InlineData("en_US", "en")]
    [InlineData("spanish", "es")]
    [InlineData("es", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("es-AR", "es")]
    [InlineData(" es_ES ", "es")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData(" pt_BR ", "pt-BR")]
    [InlineData("brazilianportuguese", "pt-BR")]
    [InlineData("pt-PT", "en")]
    [InlineData("pt", "en")]
    [InlineData("russian", "ru")]
    [InlineData("ru", "ru")]
    [InlineData("ru-RU", "ru")]
    [InlineData("ru-BY", "ru")]
    [InlineData(" ru_KZ ", "ru")]
    [InlineData("french", "fr")]
    [InlineData("fr-FR", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("japanese", "ja")]
    [InlineData("ja-JP", "ja")]
    [InlineData("indonesian", "id")]
    [InlineData("id-ID", "id")]
    [InlineData(null, "en")]
    [InlineData(" ", "en")]
    [InlineData("invalid/culture", "en")]
    [InlineData("unsupported", "en")]
    public void InstallerAndUserLanguageNames_AreNormalized(string? input, string expected) =>
        Assert.Equal(expected, L.NormalizeLanguage(input));

    [Fact]
    public void Catalog_HasUniqueNames_ValidCultures_AndAnEnglishDefault()
    {
        Assert.Equal("en", AppLanguages.Default.Code);
        Assert.Contains(AppLanguages.Default, AppLanguages.Supported);
        var names = AppLanguages.Supported.SelectMany(language => language.Aliases.Prepend(language.Code)).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var language in AppLanguages.Supported)
        {
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName));
            Assert.Equal(language.Code, CultureInfo.GetCultureInfo(language.Code).Name);
            Assert.Same(language, AppLanguages.Resolve(language.Code));
        }
    }

    [Fact]
    public void InstallerLanguages_MatchTheCatalogAndPersistCanonicalCodes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "installer", "FrameShift.iss")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var script = FrameShift.LocalizationTool.InstallerMessages.ReadSource(Path.Combine(directory.FullName, "installer", "FrameShift.iss"));
        var section = Regex.Match(script, @"(?ms)^\[Languages\]\r?\n(.*?)(?=^\[)").Groups[1].Value;
        var codes = new List<string>();
        foreach (Match match in Regex.Matches(section, @"Name:\s*""([^""]+)"""))
        {
            var installerName = match.Groups[1].Value;
            var marker = Regex.Match(script, @"(?m)^" + Regex.Escape(installerName) + @"\.AppLanguage=(\S+)\s*$");
            Assert.True(marker.Success, installerName);
            var code = marker.Groups[1].Value;
            Assert.Equal(code, AppLanguages.Resolve(installerName).Code);
            Assert.Equal(code, AppLanguages.Resolve(code).Code);
            codes.Add(code);
        }
        Assert.Equal(AppLanguages.Supported.Select(language => language.Code).Order(), codes.Order());
    }

    [Fact]
    public void InstallerMessages_AreCompleteInEveryLanguage_AndKeepTheirArguments()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "installer", "FrameShift.iss")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var script = FrameShift.LocalizationTool.InstallerMessages.ReadSource(Path.Combine(directory.FullName, "installer", "FrameShift.iss"));
        var section = Regex.Match(script, @"(?ms)^\[CustomMessages\]\r?\n(.*?)(?=^\[)").Groups[1].Value;
        var entries = Regex.Matches(section, @"(?m)^(\w+)\.(\w+)=(.*)\r?$")
            .GroupBy(match => match.Groups[1].Value)
            .ToDictionary(group => group.Key, group => group.ToDictionary(match => match.Groups[2].Value, match => match.Groups[3].Value.TrimEnd('\r')));
        var english = entries["english"];
        var installerNames = Regex.Matches(Regex.Match(script, @"(?ms)^\[Languages\]\r?\n(.*?)(?=^\[)").Groups[1].Value,
            @"Name:\s*""([^""]+)""").Select(match => match.Groups[1].Value);
        foreach (var name in installerNames)
        {
            var translated = entries[name];
            Assert.Equal(english.Keys.Order(), translated.Keys.Order());
            foreach (var key in english.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(translated[key]), name + "." + key);
                // The whitespace macro is punctuation/layout, not a runtime argument;
                // Chinese labels deliberately use a colon without an added space.
                var expected = Regex.Matches(english[key].Replace("{#InstallerSpace}", " "), @"\{[^}]+\}|%[1-9n]").Select(match => match.Value).Order();
                var actual = Regex.Matches(translated[key].Replace("{#InstallerSpace}", " "), @"\{[^}]+\}|%[1-9n]").Select(match => match.Value).Order();
                Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [InlineData("zh-Hans-CN", "zh-CN")]
    [InlineData("es-MX", "es")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("ru-RU", "ru")]
    public void DisplayLanguageInitialization_LeavesNumericCultureUnchanged(string language, string expected)
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var numericCulture = CultureInfo.CurrentCulture;
        try
        {
            L.Initialize(language);
            Assert.Equal(expected, CultureInfo.CurrentUICulture.Name);
            Assert.Equal(expected, CultureInfo.DefaultThreadCurrentUICulture!.Name);
            Assert.Same(numericCulture, CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("zh-CN", "视频", "转换")]
    [InlineData("es", "video", "Convertir")]
    [InlineData("pt-BR", "vídeo", "Converter")]
    [InlineData("ru", "видео", "Преобразовать")]
    [InlineData("fr", "vidéo", "Convertir")]
    [InlineData("ja", "動画", "変換")]
    [InlineData("id", "video", "Konversi")]
    public void LocalizedMessages_PreserveUnicodePaths_AndInternalActionValues(string language, string mediaName, string actionName)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            const string source = @"C:\测试 médias проверка\été vidéo.mp4";
            Assert.Contains(source, MediaActionMessages.InputFileNotFound(source));
            Assert.Contains(mediaName, MediaActionMessages.FfmpegFailure("video"));
            Assert.True(ActionRegistry.CreateDefault().TryGet("convert-video", out var action));
            Assert.Equal("convert-video", action.Descriptor.Id);
            Assert.Contains(actionName, action.Descriptor.DisplayName);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("en", "FrameShift - Settings", "System")]
    [InlineData("zh-CN", "FrameShift - 设置", "跟随系统")]
    [InlineData("es", "FrameShift - Configuración", "Sistema")]
    [InlineData("pt-BR", "FrameShift - Configurações", "Sistema")]
    [InlineData("ru", "FrameShift - Настройки", "Системная")]
    [InlineData("fr", "FrameShift - Paramètres", "Système")]
    [InlineData("ja", "FrameShift - 設定", "システム設定に従う")]
    [InlineData("id", "FrameShift - Pengaturan", "Sistem")]
    public void Settings_RenderInSupportedLanguages_WithStablePreferenceValues(string language, string title, string systemTheme)
    {
        StaTest.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var settingsPath = Path.Combine(Path.GetTempPath(), "FrameShift language " + Guid.NewGuid().ToString("N"), "ui-settings.json");
                using var form = new SettingsForm(settingsPath, _ => Task.FromResult(ExplorerMenuSyncResult.Updated));
                Assert.Equal(title, form.Text);
                var controls = Descendants(form).ToArray();
                var selector = Assert.Single(controls.OfType<ComboBox>(), c => c.Name == "languagePreference");
                Assert.Equal(AppLanguages.Supported, selector.Items.Cast<AppLanguage>());
                var selectedLanguage = Assert.IsType<AppLanguage>(selector.SelectedItem);
                Assert.Equal(language, selectedLanguage.Code);
                Assert.Equal(selectedLanguage.NativeName, selector.Text);
                Assert.False(string.IsNullOrWhiteSpace(selector.Text));
                var theme = Assert.Single(controls.OfType<ComboBox>(), c => c.Name == "themePreference");
                Assert.IsType<FrameShiftThemePreference>(theme.Items[0]);
                Assert.False(string.IsNullOrWhiteSpace(theme.Text));
                Assert.Equal(systemTheme, theme.GetItemText(theme.Items[0]));
                _ = form.Handle;
                foreach (var control in controls) _ = control.Handle;
                FrameShiftDialogLayout.FitInitialHeight(form, form.Controls.OfType<TableLayoutPanel>().Single());
                Assert.True(selector.Bottom <= selector.Parent!.ClientSize.Height);
                var target = Environment.GetEnvironmentVariable("FRAMESHIFT_LOCALIZATION_SCREENSHOTS");
                if (!string.IsNullOrWhiteSpace(target))
                {
                    Directory.CreateDirectory(target);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(target, "settings-" + language + ".png"));
                }
                Assert.False(form.Visible);
                Assert.False(File.Exists(settingsPath));
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Settings_ReopenPendingLanguage_AndAllowReturningToTheActiveLanguage(string language)
    {
        StaTest.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            var directory = Path.Combine(Path.GetTempPath(), "FrameShift language été 中文 русский " + Guid.NewGuid().ToString("N"));
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                var path = Path.Combine(directory, "ui-settings.json");
                new FrameShiftUiSettings { Language = "en", Theme = "Dark", OcrModel = "tiny", JoinVideosSortOrder = "Natural" }.Save(path);
                using (var first = new SettingsForm(path, _ => Task.FromResult(ExplorerMenuSyncResult.Updated)))
                {
                    var selector = Assert.Single(Descendants(first).OfType<ComboBox>(), c => c.Name == "languagePreference");
                    selector.SelectedItem = AppLanguages.Resolve(language);
                }
                Assert.Equal(language, FrameShiftUiSettings.Load(path).Language);

                using (var reopened = new SettingsForm(path, _ => Task.FromResult(ExplorerMenuSyncResult.Updated)))
                {
                    var selector = Assert.Single(Descendants(reopened).OfType<ComboBox>(), c => c.Name == "languagePreference");
                    Assert.Equal(language, Assert.IsType<AppLanguage>(selector.SelectedItem).Code);
                    Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
                    selector.SelectedItem = AppLanguages.Default;
                }
                var saved = FrameShiftUiSettings.Load(path);
                Assert.Equal("en", saved.Language);
                Assert.Equal("Dark", saved.Theme);
                Assert.Equal("tiny", saved.OcrModel);
                Assert.Equal("Natural", saved.JoinVideosSortOrder);
                Assert.Equal("en", CultureInfo.CurrentUICulture.Name);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Theory]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("es-MX", "es")]
    [InlineData("pt_br", "pt-BR")]
    [InlineData("ru_KZ", "ru")]
    public void LanguagePreference_RoundTrips_WithoutDroppingExistingSettings(string language, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrameShift language été 中文 " + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            new FrameShiftUiSettings { Language = language, Theme = "Dark", OcrModel = "tiny", JoinVideosSortOrder = "Natural" }.Save(path);
            var loaded = FrameShiftUiSettings.Load(path);
            Assert.Equal(expected, loaded.Language);
            Assert.Equal("Dark", loaded.Theme);
            Assert.Equal("tiny", loaded.OcrModel);
            Assert.Equal("Natural", loaded.JoinVideosSortOrder);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
