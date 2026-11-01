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
        var script = File.ReadAllText(Path.Combine(directory.FullName, "installer", "FrameShift.iss"));
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
    public void DisplayLanguageInitialization_LeavesNumericCultureUnchanged()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var numericCulture = CultureInfo.CurrentCulture;
        try
        {
            L.Initialize("zh-Hans-CN");
            Assert.Equal("zh-CN", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("zh-CN", CultureInfo.DefaultThreadCurrentUICulture!.Name);
            Assert.Same(numericCulture, CultureInfo.CurrentCulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void ChineseMessages_PreserveUnicodePaths_AndInternalActionValues()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            const string source = @"C:\测试 médias\été vidéo.mp4";
            Assert.Contains(source, MediaActionMessages.InputFileNotFound(source));
            Assert.Contains("视频", MediaActionMessages.FfmpegFailure("video"));
            Assert.True(ActionRegistry.CreateDefault().TryGet("convert-video", out var action));
            Assert.Equal("convert-video", action.Descriptor.Id);
            Assert.Contains("转换", action.Descriptor.DisplayName);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("en", "FrameShift - Settings")]
    [InlineData("zh-CN", "FrameShift - 设置")]
    public void Settings_RenderInBothLanguages_WithStablePreferenceValues(string language, string title)
    {
        StaTest.Run(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                using var form = new SettingsForm();
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
                Assert.Equal(language == "zh-CN" ? "跟随系统" : "System", theme.GetItemText(theme.Items[0]));
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
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    [Fact]
    public void LanguagePreference_RoundTrips_WithoutDroppingExistingSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FrameShift language été 中文 " + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            new FrameShiftUiSettings { Language = "zh-Hans", Theme = "Dark", OcrModel = "tiny", JoinVideosSortOrder = "Natural" }.Save(path);
            var loaded = FrameShiftUiSettings.Load(path);
            Assert.Equal("zh-CN", loaded.Language);
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
