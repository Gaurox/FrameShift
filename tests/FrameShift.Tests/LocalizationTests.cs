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
    [Fact]
    public void ChineseResources_AreComplete_AndRetainEveryFormatArgument()
    {
        var manager = new ResourceManager("FrameShift.Core.Localization.Strings", typeof(MediaActionMessages).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var chinese = manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false);
        Assert.NotNull(chinese);
        Assert.True(english.Cast<DictionaryEntry>().Count() > 1700);
        Assert.Equal(english.Cast<DictionaryEntry>().Count(), chinese.Cast<DictionaryEntry>().Count());
        foreach (DictionaryEntry entry in english)
        {
            var translated = chinese.GetString((string)entry.Key);
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
    [InlineData("unsupported", "en")]
    public void InstallerAndUserLanguageNames_AreNormalized(string input, string expected) =>
        Assert.Equal(expected, L.NormalizeLanguage(input));

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
                Assert.Equal(language == "zh-CN" ? 1 : 0, selector.SelectedIndex);
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
