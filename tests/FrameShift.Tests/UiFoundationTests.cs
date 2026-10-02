using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class UiFoundationTests
{
    private readonly ITestOutputHelper _output;
    public UiFoundationTests(ITestOutputHelper output) => _output = output;

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [Fact]
    public void HiddenWindow_UsesPolicyAndNativeHandles_InPerMonitorV2Context()
    {
        StaTest.Run(() =>
        {
            var previous = SetThreadDpiAwarenessContext(new nint(-4));
            Assert.NotEqual(nint.Zero, previous);
            try
            {
                using var form = new Form();
                form.SuspendLayout();
                FrameShiftWindowPolicy.Initialize(form, new Size(740, 620), new Size(400, 300));
                var header = FrameShiftUiFactory.CreateHeader("FrameShift — Test des primitives", "Source avec espaces et accents.mp4", "", "", "▶");
                var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Annuler", false);
                var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Confirmer", true);
                var actions = FrameShiftDialogLayout.CreateActions(cancel, primary);
                var root = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateSection("Options", CreateFields(15)), actions);
                form.Controls.Add(root);
                form.ResumeLayout(true);
                CreateHiddenHandles(form);
                form.PerformAutoScale();
                LayoutTree(form);
                _output.WriteLine($"Hidden native handles: DeviceDpi={form.DeviceDpi}; client={form.ClientSize}; AutoScaleDimensions={form.AutoScaleDimensions}; thread=PerMonitorV2");
                Assert.False(form.Visible);
                Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                Assert.Equal(form.DeviceDpi, (int)form.AutoScaleDimensions.Width);
                Assert.True(header.SubtitleLabel.Bottom <= header.Height);
                Assert.True(actions.ClientRectangle.Contains(primary.Bounds));
                Assert.Equal(new Size(140, 34), primary.Size);
                Assert.Equal(primary.Size, cancel.Size);
                Assert.True(actions.Bottom <= root.ClientSize.Height);

                // Add a control after handles/scaling, without calling Scale() on its subtree.
                var dynamicEditor = new TextBox { Text = "Valeur dynamique" };
                var dynamicRow = FrameShiftUiFactory.CreateFieldRow("Champ ajouté", dynamicEditor);
                var fields = CreateFields(1);
                var viewport = Assert.IsType<Panel>(root.GetControlFromPosition(0, 1));
                var oldContent = viewport.Controls[0];
                viewport.Controls.Remove(oldContent);
                oldContent.Dispose();
                fields.Controls.Add(dynamicRow, 0, 1);
                viewport.Controls.Add(fields);
                CreateHiddenHandles(fields);
                LayoutTree(form);
                Assert.Equal(form.DeviceDpi, dynamicEditor.DeviceDpi);
                Assert.True(dynamicRow.ClientRectangle.Contains(dynamicEditor.Bounds));
                Assert.False(form.Visible);
            }
            finally { SetThreadDpiAwarenessContext(previous); }
        });
    }

    private static void CreateHiddenHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
    }

    [Theory]
    [InlineData(96, 12, 34)]
    [InlineData(144, 18, 51)]
    [InlineData(192, 24, 68)]
    [InlineData(288, 36, 102)]
    public void LogicalMetrics_AndTextMeasurements_AreNotScaledTwice(int dpi, int padding, int height)
    {
        Assert.Equal(padding, FrameShiftUiMetrics.ToPixels(12, dpi));
        var pixels = new Size(800, 100); // Already measured text, deliberately larger than the minima.
        var measured = FrameShiftUiLayout.MeasureActionButton(pixels, dpi, 140);
        Assert.Equal(800 + FrameShiftUiMetrics.ToPixels(28, dpi), measured.Width);
        Assert.Equal(100 + FrameShiftUiMetrics.ToPixels(14, dpi), measured.Height);
        Assert.Equal(height, FrameShiftUiLayout.MeasureActionButton(Size.Empty, dpi, 140).Height);
    }

    [Fact]
    public void RepeatedDpiConversions_ReturnToOriginalDimensions()
    {
        var logical = new Size(258, 58);
        for (var pass = 0; pass < 10; pass++)
        {
            foreach (var dpi in new[] { 96, 192, 144, 288, 96 })
                Assert.Equal(new Size(258 * dpi / 96, 58 * dpi / 96), FrameShiftUiMetrics.ToPixels(logical, dpi));
        }
        Assert.Equal(new Size(258, 58), logical);
    }

    [Fact]
    public void WindowBounds_FitSmallAndNegativeOriginWorkingAreas()
    {
        var area = new Rectangle(-1280, -200, 1280, 672);
        Assert.Equal(area, FrameShiftWindowPolicy.FitBounds(new Rectangle(-2000, -1000, 2400, 1800), area));
        var fitted = FrameShiftWindowPolicy.FitBounds(new Rectangle(200, 300, 600, 300), area);
        Assert.True(area.Contains(fitted));
        Assert.Equal(new Size(600, 300), fitted.Size);
    }

    [Theory]
    [InlineData(1F)]
    [InlineData(1.5F)]
    [InlineData(2F)]
    [InlineData(3F)]
    public void HeaderAndCompactShell_MeasureLongText_KeepFooterOutsideScroll(float textScale)
    {
        // Font enlargement exercises measurement; it is NOT an OS DPI transition test.
        StaTest.Run(() =>
        {
            using var font = new Font("Segoe UI", 9 * textScale);
            using var titleFont = new Font("Segoe UI Semibold", 14 * textScale);
            using var host = new Panel { Size = new Size(740, 620), Font = font };
            var header = FrameShiftUiFactory.CreateHeader("FrameShift — Une fonction avec un titre long et des accents",
                @"Source : E:\vidéos avec espaces\un fichier extrêmement long.mp4", "", "", "▶");
            header.TitleLabel.Font = titleFont;
            var fields = CreateFields(18);
            var section = FrameShiftUiFactory.CreateSection("Options de traitement", fields);
            var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Annuler", false);
            var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Démarrer le traitement", true);
            var actions = FrameShiftDialogLayout.CreateActions(cancel, primary);
            var message = FrameShiftUiFactory.CreateStatusMessage(string.Join(Environment.NewLine, Enumerable.Repeat("Une erreur longue doit rester entièrement consultable et copiable.", 50)));
            var root = FrameShiftDialogLayout.Create(header, section, actions, message);
            host.Controls.Add(root);
            LayoutTree(host);

            Assert.True(header.TitleLabel.Bottom <= header.SubtitleLabel.Top);
            Assert.True(header.SubtitleLabel.Bottom <= header.Height);
            Assert.True(actions.Bottom <= root.ClientSize.Height - root.Padding.Bottom);
            Assert.True(Rectangle.Intersect(cancel.Bounds, primary.Bounds).IsEmpty);
            Assert.True(actions.ClientRectangle.Contains(primary.Bounds));
            Assert.True(actions.ClientRectangle.Contains(cancel.Bounds));
            Assert.Equal(primary.Size, cancel.Size);
            var viewport = Assert.IsType<Panel>(root.GetControlFromPosition(0, 1));
            Assert.True(viewport.AutoScroll);
            Assert.True(viewport.Bottom <= actions.Top);
            Assert.True(section.Height > viewport.ClientSize.Height);
            Assert.Same(root, actions.Parent);
            Assert.True(message.ReadOnly);
            Assert.Equal(ScrollBars.Vertical, message.ScrollBars);
            Assert.True(message.Bottom <= actions.Top);
            Assert.True(message.Height <= FrameShiftUiMetrics.ToPixels(message, 96));

            // Dynamic content after initial layout, including a much longer action caption.
            primary.Text = "Confirmer cette opération avec le préréglage sélectionné";
            header.SubtitleLabel.Text = "Métadonnées modifiées après construction";
            LayoutTree(host);
            Assert.Equal(primary.Size, cancel.Size);
            Assert.True(actions.ClientRectangle.Contains(primary.Bounds), $"scale={textScale}; primary={primary.Bounds}; actions={actions.Bounds}");
            Assert.True(actions.Bottom <= root.ClientSize.Height - root.Padding.Bottom);
            Assert.True(header.SubtitleLabel.Bottom <= header.Height);
        });
    }

    [Fact]
    public void Editor_ReflowsOptions_AndRestoresOriginalColumns()
    {
        StaTest.Run(() =>
        {
            using var host = new Panel { Size = new Size(1000, 650) };
            var preview = new Panel();
            var options = FrameShiftUiFactory.CreateSection("Options", CreateFields(25));
            var workspace = new FrameShiftEditorWorkspace(preview, options, 258);
            host.Controls.Add(workspace);
            LayoutTree(host);
            var original = preview.Bounds;
            Assert.Equal(2, workspace.ColumnCount);
            for (var pass = 0; pass < 4; pass++)
            {
                host.Width = 480;
                LayoutTree(host);
                Assert.Equal(1, workspace.ColumnCount);
                Assert.True(options.Parent!.Bottom <= workspace.ClientSize.Height);
                Assert.True(options.Parent.Top >= preview.Bottom);
                Assert.True(options.Height > options.Parent.ClientSize.Height);
                host.Width = 1000;
                LayoutTree(host);
                Assert.Equal(2, workspace.ColumnCount);
                Assert.Equal(original, preview.Bounds);
            }
        });
    }

    [Fact]
    public void DynamicField_HasAccessibleName_AndGrowsForText()
    {
        StaTest.Run(() =>
        {
            // Compare two explicit fonts; the process/Windows default is not a stable baseline.
            using var initialFont = new Font("Segoe UI", 9);
            using var host = new Panel { Size = new Size(700, 250), Font = initialFont };
            var editor = new TextBox { Text = "00:00:04.733" };
            var row = FrameShiftUiFactory.CreateFieldRow("&Durée", editor, "secondes");
            host.Controls.Add(row);
            LayoutTree(host);
            var initial = row.Height;
            using var font = new Font("Segoe UI", 20);
            host.Font = font;
            LayoutTree(host);
            Assert.Equal("Durée", editor.AccessibleName);
            Assert.True(row.Height > initial, $"Initial height={initial}, final={row.Height}, host font={host.Font}, editor font={editor.Font}, dpi={host.DeviceDpi}");
            Assert.True(editor.Left >= row.Controls[0].Right);
            Assert.True(editor.Right <= row.Controls[2].Left);
            Assert.True(row.ClientRectangle.Contains(editor.Bounds));
            row.Controls[0].Text = "Libellé très long avec espaces et accents qui doit laisser la saisie utilisable";
            LayoutTree(host);
            Assert.True(editor.Width > 200);
            Assert.True(row.Controls[0].Height > editor.Height);
        });
    }

    [Fact]
    public void StatusMessage_ScrollbarChanges_AreSafeDuringNativeResizeAndDisposal()
    {
        StaTest.Run(() =>
        {
            using var host = new Panel { Size = new Size(400, 80) };
            var status = FrameShiftUiFactory.CreateStatusMessage("Short message");
            host.Controls.Add(status);
            CreateHiddenHandles(host);
            for (var i = 0; i < 10; i++)
            {
                status.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Long details that must stay available", 20));
                host.Size = new Size(220 + i * 10, 60);
                LayoutTree(host);
                Application.DoEvents();
                Assert.Equal(ScrollBars.Vertical, status.ScrollBars);
                status.Text = "Short message";
                host.Size = new Size(400, 80);
                LayoutTree(host);
                Application.DoEvents();
                Assert.Equal(ScrollBars.None, status.ScrollBars);
            }
            status.Text = new string('x', 5000);
            host.Width = 240;
            host.Dispose();
            Application.DoEvents(); // A queued scrollbar change must not recreate a disposed HWND.
        });
    }

    [Fact]
    public void RoundedPainter_AcceptsTinySurfacesAndLargeLogicalRadius()
    {
        StaTest.Run(() =>
        {
            using var panel = new Panel { Size = new Size(2, 2) };
            using var bitmap = new Bitmap(2, 2);
            using var graphics = Graphics.FromImage(bitmap);
            FrameShiftUiPainter.DrawRoundedBorder(panel, graphics, Color.Blue, 24);
            using var path = FrameShiftUiPainter.CreateRoundedPath(new Rectangle(0, 0, 7, 5), 100);
            Assert.True(new RectangleF(-0.001F, -0.001F, 7.002F, 5.002F).Contains(path.GetBounds()));
        });
    }

    private static TableLayoutPanel CreateFields(int count)
    {
        var fields = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
            Dock = DockStyle.Top, Margin = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < count; i++)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(FrameShiftUiFactory.CreateFieldRow($"&Option {i}", new TextBox { Text = "Texte avec accents" }), 0, i);
        }
        return fields;
    }

    private static void LayoutTree(Control control)
    {
        // No Show(), desktop capture, keyboard/mouse injection, media loading or message loop.
        control.PerformLayout();
        foreach (Control child in control.Controls) LayoutTree(child);
        control.PerformLayout();
    }
}
