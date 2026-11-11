using System.Globalization;
using System.Windows.Forms;
using FrameShift.Core.Localization;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class FrameShiftLanguageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FrameShift installation langue été 日本 " + Guid.NewGuid().ToString("N"));
    private string Installation => Path.Combine(_root, "app");
    private string Settings => Path.Combine(_root, "user", "ui-settings.json");

    public static IEnumerable<object[]> Languages => AppLanguages.Supported.Select(item => new object[] { item.Code });

    public FrameShiftLanguageTests() => Directory.CreateDirectory(Installation);

    [Theory]
    [MemberData(nameof(Languages))]
    public void UpgradeFromSavedEnglish_AppliesInstallerLanguageAndPreservesOtherPreferences(string language)
    {
        new FrameShiftUiSettings
        {
            Language = "en", Theme = "Dark", JoinVideosSortOrder = "Natural",
            OcrModel = "tiny", OcrFormat = "pdf", OcrParagraphs = true, OcrDpi = 300
        }.Save(Settings);
        var id = Install(language);

        Assert.Equal(language, Initialize());
        var saved = FrameShiftUiSettings.Load(Settings);
        Assert.Equal(language, saved.Language);
        Assert.Equal(id, saved.AppliedLanguageInstallationId);
        Assert.Equal("Dark", saved.Theme);
        Assert.Equal("Natural", saved.JoinVideosSortOrder);
        Assert.Equal("tiny", saved.OcrModel);
        Assert.Equal("pdf", saved.OcrFormat);
        Assert.True(saved.OcrParagraphs);
        Assert.Equal(300, saved.OcrDpi);
    }

    [Fact]
    public void FirstLaunchAfterEnglishUpgradeRendersFrenchSettingsAndSelectsFrench()
    {
        new FrameShiftUiSettings { Language = "en" }.Save(Settings);
        Install("fr");
        StaTest.Run(() => Assert.Equal("fr", Initialize(inspectUi: () =>
        {
            using var form = new SettingsForm(Settings, _ => Task.FromResult(ExplorerMenuSyncResult.Updated));
            Assert.Equal("FrameShift - Paramètres", form.Text);
            var selector = Assert.IsType<ComboBox>(Assert.Single(form.Controls.Find("languagePreference", true)));
            Assert.Equal("fr", Assert.IsType<AppLanguage>(selector.SelectedItem).Code);
        })));
    }

    [Fact]
    public void LaterUserChoiceSurvivesRestartsAndOtherPreferenceSaves()
    {
        var id = Install("fr");
        Assert.Equal("fr", Initialize());
        var settings = FrameShiftUiSettings.Load(Settings);
        settings.Language = "ja";
        settings.Save(Settings);
        Assert.Equal("ja", Initialize());

        settings = FrameShiftUiSettings.Load(Settings);
        settings.Theme = "Light";
        settings.Save(Settings);
        Assert.Equal("ja", Initialize());
        Assert.Equal(id, FrameShiftUiSettings.Load(Settings).AppliedLanguageInstallationId);
    }

    [Fact]
    public void ReinstallSameVersionAndLanguage_AppliesTheNewChoiceAgain()
    {
        var firstId = Install("fr");
        Assert.Equal("fr", Initialize());
        var settings = FrameShiftUiSettings.Load(Settings);
        settings.Language = "en";
        settings.Save(Settings);
        Assert.Equal("en", Initialize());

        // No version change: the new installation event alone resets the choice.
        var secondId = Install("fr");
        Assert.NotEqual(firstId, secondId);
        Assert.Equal("fr", Initialize());
        Assert.Equal(secondId, FrameShiftUiSettings.Load(Settings).AppliedLanguageInstallationId);
    }

    [Fact]
    public void EachWindowsUserConsumesTheInstallationIndependently()
    {
        var secondUser = Path.Combine(_root, "second user", "ui-settings.json");
        new FrameShiftUiSettings { Language = "en", Theme = "Dark" }.Save(Settings);
        new FrameShiftUiSettings { Language = "es", Theme = "Light" }.Save(secondUser);
        var id = Install("id");
        Assert.Equal("id", Initialize());
        Assert.Equal("es", FrameShiftUiSettings.Load(secondUser).Language);
        Assert.Equal("id", Initialize(secondUser));
        Assert.Equal(id, FrameShiftUiSettings.Load(secondUser).AppliedLanguageInstallationId);
        Assert.Equal("Light", FrameShiftUiSettings.Load(secondUser).Theme);

        var firstUser = FrameShiftUiSettings.Load(Settings);
        firstUser.Language = "ru";
        firstUser.Save(Settings);
        Assert.Equal("ru", Initialize());
        Assert.Equal("id", Initialize(secondUser));
    }

    [Theory]
    [InlineData("en", "ru", "en")]
    [InlineData(null, "ru", "ru")]
    [InlineData(null, null, "en")]
    public void LegacyOrPortableInstallationKeepsThePreviousFallbackOrder(string? preference, string? installed, string expected)
    {
        if (preference is not null) new FrameShiftUiSettings { Language = preference }.Save(Settings);
        if (installed is not null) File.WriteAllText(Path.Combine(Installation, "language.txt"), installed);
        Assert.Equal(expected, Initialize());
        Assert.Null(FrameShiftUiSettings.Load(Settings).AppliedLanguageInstallationId);
    }

    [Theory]
    [InlineData("not-a-guid\r\nfr")]
    [InlineData("00000000000000000000000000000000\r\nfr")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef\r\nunsupported")]
    [InlineData("0123456789abcdef0123456789abcdef\r\nfrench")]
    [InlineData("0123456789abcdef0123456789abcdef\r\nfr\r\nextra")]
    public void MalformedInstallationMarkerDoesNotReplacePersonalPreferences(string content)
    {
        new FrameShiftUiSettings { Language = "es", Theme = "Dark" }.Save(Settings);
        var original = File.ReadAllBytes(Settings);
        File.WriteAllText(Path.Combine(Installation, FrameShiftLanguage.InstallationFileName), content);
        Assert.Equal("es", Initialize());
        Assert.Equal(original, File.ReadAllBytes(Settings));
    }

    [Fact]
    public void MarkerContainsItsOwnLanguageRatherThanDependingOnTheLegacyFile()
    {
        Install("fr");
        File.WriteAllText(Path.Combine(Installation, "language.txt"), "en");
        Assert.Equal("fr", Initialize());
    }

    [Fact]
    public void UnreadableMarkerDoesNotBlockStartupOrAcknowledgeTheChoice()
    {
        new FrameShiftUiSettings { Language = "es" }.Save(Settings);
        Install("fr");
        using (File.Open(Path.Combine(Installation, FrameShiftLanguage.InstallationFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal("es", Initialize());
            Assert.Null(FrameShiftUiSettings.Load(Settings).AppliedLanguageInstallationId);
        }
        Assert.Equal("fr", Initialize());
    }

    [Fact]
    public void ReadOnlyPreferencesApplyLanguageInMemoryAndRetrySavingOnNextLaunch()
    {
        new FrameShiftUiSettings { Language = "en", Theme = "Dark" }.Save(Settings);
        var original = File.ReadAllBytes(Settings);
        var id = Install("fr");
        File.SetAttributes(Settings, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal("fr", Initialize());
            Assert.Equal(original, File.ReadAllBytes(Settings));
        }
        finally { File.SetAttributes(Settings, FileAttributes.Normal); }
        Assert.Equal("fr", Initialize());
        Assert.Equal(id, FrameShiftUiSettings.Load(Settings).AppliedLanguageInstallationId);
        Assert.Equal("Dark", FrameShiftUiSettings.Load(Settings).Theme);
    }

    private string Install(string language)
    {
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(Installation, "language.txt"), language);
        File.WriteAllText(Path.Combine(Installation, FrameShiftLanguage.InstallationFileName), id + "\r\n" + language);
        return id;
    }

    private string Initialize(string? settings = null, Action? inspectUi = null)
    {
        var ui = CultureInfo.CurrentUICulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var numeric = CultureInfo.CurrentCulture;
        try
        {
            FrameShiftLanguage.Initialize(settings ?? Settings, Installation);
            Assert.Same(numeric, CultureInfo.CurrentCulture);
            Assert.Equal(CultureInfo.CurrentUICulture, CultureInfo.DefaultThreadCurrentUICulture);
            var code = CultureInfo.CurrentUICulture.Name;
            inspectUi?.Invoke();
            return code;
        }
        finally { CultureInfo.DefaultThreadCurrentUICulture = defaultUi; CultureInfo.CurrentUICulture = ui; }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
