using FrameShift.Core.AI.Ocr;
using FrameShift.Core.Actions;
using FrameShift.Windows.AI;
using System.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class OcrUiTests
{
    [Theory]
    [InlineData("txt")]
    [InlineData("pdf")]
    [InlineData("json")]
    public void PickerUsesNativeControlsKeyboardAndSharedWindowPolicy(string format)
    {
        StaTest.Run(() =>
        {
            using var form = new ExtractTextPickerForm(() => ["invoice été.pdf"], new OcrSettings(Format: format));
            _ = form.Handle;
            form.PerformLayout();
            Assert.Equal("FrameShift - Extract Text", form.Text);
            Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
            Assert.Equal("Extract Text", ((Button)form.AcceptButton!).Text);
            Assert.Equal(DialogResult.Cancel, form.CancelButton!.DialogResult);
            IEnumerable<Control> Controls(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Controls(child)));
            var choices = Controls(form).OfType<ComboBox>().ToArray();
            Assert.Equal(7, choices.Length);
            Assert.All(choices, c => Assert.Equal(ComboBoxStyle.DropDownList, c.DropDownStyle));
            Assert.Contains(Controls(form).OfType<TextBox>(), t => t.PlaceholderText.Contains("1-3,5"));
            Assert.False(string.IsNullOrWhiteSpace(((Button)form.AcceptButton!).AccessibleName));
        });
    }

    [Fact]
    public void OcrIsAvailableForMixedImagePdfSelection()
    {
        Assert.True(ActionCatalog.TryGet("extract-text", out var entry));
        Assert.True(entry.Accepts(".pdf"));
        Assert.True(entry.Accepts(".png"));
        Assert.False(entry.Accepts(".mp4"));
        Assert.Equal(MediaFamily.Document, MediaFileClassifier.Classify("invoice.PDF"));
    }

    [Fact]
    public void ExplicitOcrCliOptionsRemainOptionsRatherThanFiles()
    {
        Assert.True(FrameShift.Program.TryParseArguments(["--action", "extract-text", "--ocr-model", "tiny", "--ocr-format", "json", "--ocr-pages", "1-3,5", "C:\\Docs\\été.pdf"], out var action, out var inputs, out var options, out var error), error);
        Assert.Equal("extract-text", action);
        Assert.Single(inputs);
        Assert.Equal("tiny", options["ocr-model"]);
        Assert.Equal("1-3,5", options["ocr-pages"]);
        Assert.False(FrameShift.Program.TryParseArguments(["--action", "extract-text", "--ocr-device"], out _, out _, out _, out _));
    }
}
