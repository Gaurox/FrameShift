using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Logging;
using FrameShift.Windows.AI;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;
using Xunit;
using Xunit.Abstractions;

namespace FrameShift.Tests;

// Hidden controls and in-memory painting only; no desktop input or preference-file writes.
public sealed class UiColorStateTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(FrameShiftThemePreference.Light)]
    [InlineData(FrameShiftThemePreference.Dark)]
    public void InformativeTextAndEnabledButtonStates_MeetTheContrastTarget(FrameShiftThemePreference preference)
    {
        WithTheme(() =>
        {
            FrameShiftTheme.ApplyPreference(preference);
            var backgrounds = new[] { FrameShiftTheme.Surface, FrameShiftTheme.PageBackground,
                FrameShiftTheme.AccentSoft, FrameShiftTheme.AccentSoftHover };
            var textColors = new[] { FrameShiftTheme.TextPrimary, FrameShiftTheme.TextSecondary,
                FrameShiftTheme.TextMuted, FrameShiftTheme.AccentText, FrameShiftTheme.ErrorText, FrameShiftTheme.SuccessText };
            foreach (var text in textColors)
                foreach (var background in backgrounds) AssertReadable(text, background);

            using var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Start", true);
            foreach (var fill in new[] { primary.BackColor, primary.FlatAppearance.MouseOverBackColor,
                         primary.FlatAppearance.MouseDownBackColor }) AssertReadable(primary.ForeColor, fill);
            using var secondary = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
            foreach (var fill in new[] { secondary.BackColor, secondary.FlatAppearance.MouseOverBackColor,
                         secondary.FlatAppearance.MouseDownBackColor }) AssertReadable(secondary.ForeColor, fill);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Buttons_PaintDistinctHoverPressAndDisabledStates_WithoutChangingGeometry(bool primary)
    {
        WithTheme(() =>
        {
            using var host = new Form { BackColor = FrameShiftTheme.PageBackground };
            var button = (FrameShiftActionButton)FrameShiftUiFactory.CreateMeasuredActionButton("Start", primary);
            host.Controls.Add(button);
            button.Size = button.GetPreferredSize(Size.Empty);
            var originalSize = button.Size;
            foreach (var preference in new[] { FrameShiftThemePreference.Light, FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light })
            {
                FrameShiftTheme.ApplyPreference(preference);
                FrameShiftTheme.ApplyToControl(host);
                AssertFill(button, button.BackColor);
                Raise(button, "OnMouseEnter", EventArgs.Empty);
                AssertFill(button, button.FlatAppearance.MouseOverBackColor);
                Assert.NotEqual(button.BackColor.ToArgb(), button.FlatAppearance.MouseOverBackColor.ToArgb());
                Raise(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 8, 0));
                AssertFill(button, button.FlatAppearance.MouseDownBackColor);
                Assert.NotEqual(button.FlatAppearance.MouseOverBackColor.ToArgb(), button.FlatAppearance.MouseDownBackColor.ToArgb());
                Raise(button, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 20, 8, 0));
                AssertFill(button, button.FlatAppearance.MouseOverBackColor);
                Raise(button, "OnMouseLeave", EventArgs.Empty);
                Raise(button, "OnKeyDown", new KeyEventArgs(Keys.Space));
                AssertFill(button, button.FlatAppearance.MouseDownBackColor);
                Raise(button, "OnLostFocus", EventArgs.Empty);
                AssertFill(button, button.BackColor);
                Raise(button, "OnMouseEnter", EventArgs.Empty);
                button.Enabled = false;
                AssertFill(button, FrameShiftTheme.PageBackground);
                button.Enabled = true;
                AssertFill(button, button.BackColor);
                Assert.Equal(originalSize, button.Size);
            }
            Assert.False(host.Visible);
        });
    }

    [Fact]
    public void SelectedRadioCards_RemainVisible_WhenDisabledAndWhenThemeChanges()
    {
        WithTheme(() =>
        {
            using var host = new Form();
            var card = new FrameShiftChoiceCard("High quality", "Preserves detail") { Checked = true };
            host.Controls.Add(card);
            card.Size = card.GetPreferredSize(Size.Empty);
            var original = card.Size;
            foreach (var preference in new[] { FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light })
            {
                FrameShiftTheme.ApplyPreference(preference);
                FrameShiftTheme.ApplyToControl(host);
                using (var bitmap = new Bitmap(card.Width, card.Height))
                {
                    card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, card.Size));
                    Assert.Equal(FrameShiftTheme.AccentSoft.ToArgb(), bitmap.GetPixel(20, card.Height - 7).ToArgb());
                    card.Enabled = false;
                    card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, card.Size));
                    Assert.Equal(FrameShiftTheme.PageBackground.ToArgb(), bitmap.GetPixel(20, card.Height - 7).ToArgb());
                }
                Assert.True(card.Checked);
                Assert.Equal(original, card.Size);
                card.Enabled = true;
            }
            Assert.False(host.Visible);
        });
    }

    [Fact]
    public void RemoveObject_SelectedToolRetainsReadableCaptions_InBothThemes()
    {
        WithTheme(() =>
        {
            using var form = new RemoveObjectEditorForm("absent.png", new AppLogger());
            var tools = Descendants(form).OfType<Button>().Where(b => b.Text is "Brush" or "Eraser").ToArray();
            Assert.Equal(2, tools.Length);
            foreach (var preference in new[] { FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light })
            {
                FrameShiftTheme.ApplyPreference(preference);
                FrameShiftTheme.ApplyToControl(form);
                foreach (var selected in tools)
                {
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(selected, [EventArgs.Empty]);
                    foreach (var button in tools)
                        foreach (var fill in new[] { button.BackColor, button.FlatAppearance.MouseOverBackColor, button.FlatAppearance.MouseDownBackColor })
                            AssertReadable(button.ForeColor, fill);
                    Assert.Equal(FrameShiftTheme.PrimaryButtonBackground, selected.BackColor);
                    Assert.Equal(Color.White, selected.ForeColor);
                }
            }
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void GridThemeChanges_RemapExistingOverrides_AndPreserveInheritanceAndCustomColors()
    {
        WithTheme(() =>
        {
            FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Light);
            using var grid = new DataGridView { AllowUserToAddRows = false };
            grid.Columns.Add("first", "First");
            grid.Columns.Add("second", "Second");
            grid.DefaultCellStyle.BackColor = FrameShiftTheme.Surface;
            grid.DefaultCellStyle.ForeColor = FrameShiftTheme.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = FrameShiftTheme.PageBackground;
            grid.DefaultCellStyle.SelectionForeColor = FrameShiftTheme.TextPrimary;
            grid.RowsDefaultCellStyle.ForeColor = FrameShiftTheme.TextSecondary;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = FrameShiftTheme.TextSecondary;
            grid.RowHeadersDefaultCellStyle.ForeColor = FrameShiftTheme.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = FrameShiftTheme.AccentText;
            grid.Columns[0].DefaultCellStyle.ForeColor = FrameShiftTheme.TextMuted;
            grid.Columns[0].HeaderCell.Style.ForeColor = FrameShiftTheme.AccentText;
            grid.Columns[0].CellTemplate!.Style.SelectionForeColor = FrameShiftTheme.TextMuted;
            grid.RowTemplate.DefaultCellStyle.SelectionBackColor = FrameShiftTheme.AccentSoft;
            grid.RowTemplate.HeaderCell.Style.ForeColor = FrameShiftTheme.TextSecondary;
            grid.Rows.Add(4);
            grid.Rows[0].DefaultCellStyle.ForeColor = FrameShiftTheme.ErrorText;
            grid.Rows[0].HeaderCell.Style.ForeColor = FrameShiftTheme.TextMuted;
            grid.Rows[1].Cells[1].Style.ForeColor = FrameShiftTheme.SuccessText;
            grid.Rows[1].Cells[1].Style.SelectionForeColor = FrameShiftTheme.SuccessText;
            var custom = Color.FromArgb(123, 91, 67);
            grid.Rows[2].Cells[1].Style.ForeColor = custom;
            var sharedIndex = grid.Rows.SharedRow(3).Index;
            foreach (var preference in new[] { FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light, FrameShiftThemePreference.Dark })
            {
                FrameShiftTheme.ApplyPreference(preference);
                FrameShiftTheme.ApplyToControl(grid);
                Assert.Equal(FrameShiftTheme.Surface, grid.DefaultCellStyle.BackColor);
                Assert.Equal(FrameShiftTheme.TextSecondary, grid.RowsDefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.TextSecondary, grid.AlternatingRowsDefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.TextPrimary, grid.RowHeadersDefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.AccentText, grid.ColumnHeadersDefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.TextMuted, grid.Columns[0].DefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.AccentText, grid.Columns[0].HeaderCell.Style.ForeColor);
                Assert.Equal(FrameShiftTheme.TextMuted, grid.Columns[0].CellTemplate!.Style.SelectionForeColor);
                Assert.Equal(FrameShiftTheme.AccentSoft, grid.RowTemplate.DefaultCellStyle.SelectionBackColor);
                Assert.Equal(FrameShiftTheme.TextSecondary, grid.RowTemplate.HeaderCell.Style.ForeColor);
                Assert.Equal(FrameShiftTheme.ErrorText, grid.Rows[0].DefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.TextMuted, grid.Rows[0].HeaderCell.Style.ForeColor);
                Assert.Equal(FrameShiftTheme.SuccessText, grid.Rows[1].Cells[1].Style.ForeColor);
                Assert.Equal(FrameShiftTheme.SuccessText, grid.Rows[1].Cells[1].Style.SelectionForeColor);
                Assert.Equal(custom, grid.Rows[2].Cells[1].Style.ForeColor);
                Assert.Equal(Color.Empty, grid.Columns[0].DefaultCellStyle.BackColor);
                Assert.False(grid.Rows.SharedRow(3).Cells[1].HasStyle);
                Assert.Equal(sharedIndex, grid.Rows.SharedRow(3).Index);
            }
        });
    }

    [Fact]
    public void Files_KindAndRemoveColumnsFollowTheme_WithoutAlternatingBands()
    {
        WithTheme(() =>
        {
            FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Light);
            using var panel = new FileQueuePanel();
            var grid = Descendants(panel).OfType<DataGridView>().Single();
            foreach (var preference in new[] { FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light })
            {
                FrameShiftTheme.ApplyPreference(preference);
                FrameShiftTheme.ApplyToControl(panel);
                Assert.Equal(FrameShiftTheme.TextMuted, grid.Columns["Kind"]!.DefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.AccentText, grid.Columns["Remove"]!.DefaultCellStyle.ForeColor);
                Assert.Equal(FrameShiftTheme.AccentText, grid.Columns["Remove"]!.DefaultCellStyle.SelectionForeColor);
                Assert.True(grid.AlternatingRowsDefaultCellStyle.BackColor.IsEmpty);
                Assert.Equal(FrameShiftTheme.Surface, grid.DefaultCellStyle.BackColor);
            }
        });
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("completed")]
    [InlineData("canceling")]
    [InlineData("processing")]
    [InlineData("canceled")]
    public void Progress_CachedSummaryAndFormattedRowsFollowTheme_WithoutResettingDetails(string state)
    {
        WithTheme(() =>
        {
            FrameShiftTheme.ApplyPreference(FrameShiftThemePreference.Light);
            using var form = new ProgressForm();
            Handles(form);
            form.ReportQueue(["Vidéo avec espaces et accents.mp4", "Autre vidéo.mp4"]);
            form.ReportQueueItem("Vidéo avec espaces et accents.mp4", state, "Diagnostic à conserver.");
            form.ReportState(state, "Diagnostic à conserver.");
            var grid = Descendants(form).OfType<DataGridView>().Single();
            var details = Descendants(form).OfType<TextBox>().Single(c => c.Name == "progressStatus");
            var before = details.Text;
            details.Select(3, 4);
            foreach (var preference in new[] { FrameShiftThemePreference.Dark, FrameShiftThemePreference.Light })
            {
                FrameShiftTheme.ApplyPreference(preference);
                FrameShiftTheme.ApplyToControl(form);
                var expected = state switch
                {
                    "failed" or "canceling" => FrameShiftTheme.ErrorText,
                    "completed" => FrameShiftTheme.SuccessText,
                    "processing" => FrameShiftTheme.AccentText,
                    _ => FrameShiftTheme.TextMuted
                };
                foreach (var name in new[] { "progressSummary", "detailsContext" })
                    Assert.Equal(expected, Descendants(form).Single(c => c.Name == name).ForeColor);
                var style = grid.Rows[0].Cells[1].InheritedStyle;
                var formatting = new DataGridViewCellFormattingEventArgs(1, 0, state, typeof(string), style);
                typeof(DataGridView).GetMethod("OnCellFormatting", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, [typeof(DataGridViewCellFormattingEventArgs)], null)!
                    .Invoke(grid, [formatting]);
                Assert.Equal(expected, style.ForeColor);
                Assert.Equal(expected, style.SelectionForeColor);
                Assert.Equal(FrameShiftTheme.Surface, grid.ColumnHeadersDefaultCellStyle.BackColor);
                Assert.Equal(grid.Rows[0].Cells[0].InheritedStyle.BackColor, grid.Rows[1].Cells[0].InheritedStyle.BackColor);
                Assert.Equal(before, details.Text);
                Assert.Equal(3, details.SelectionStart);
                Assert.Equal(4, details.SelectionLength);
            }
            Assert.False(form.Visible);
        });
    }

    private void AssertReadable(Color foreground, Color background)
    {
        var a = Luminance(foreground);
        var b = Luminance(background);
        var contrast = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        output.WriteLine($"{FrameShiftTheme.EffectiveTheme}: #{foreground.ToArgb() & 0xFFFFFF:X6} / #{background.ToArgb() & 0xFFFFFF:X6} = {contrast:F3}:1");
        Assert.True(contrast >= 4.5, $"#{foreground.ToArgb():X8} / #{background.ToArgb():X8}: {contrast:F3}:1");
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static void AssertFill(FrameShiftActionButton button, Color expected)
    {
        using var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, button.Size));
        Assert.Equal(expected.ToArgb(), bitmap.GetPixel(20, 7).ToArgb());
    }

    private static void Raise(FrameShiftActionButton button, string method, object args) =>
        typeof(FrameShiftActionButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [args]);

    private static void WithTheme(Action action) => StaTest.Run(() =>
    {
        var previous = FrameShiftTheme.EffectiveTheme;
        try { action(); }
        finally { FrameShiftTheme.ApplyPreference(previous == FrameShiftThemeMode.Dark ? FrameShiftThemePreference.Dark : FrameShiftThemePreference.Light); }
    });

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static void Handles(Control control) { _ = control.Handle; foreach (Control child in control.Controls) Handles(child); }
}
