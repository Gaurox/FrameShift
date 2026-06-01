using System;
using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal enum CompressMultiFileChoice { SameForAll, PerFile }

internal sealed class CompressMultiFileChoiceForm : Form
{
    private CompressMultiFileChoice _choice = CompressMultiFileChoice.SameForAll;
    private readonly Button _cancelButton;
    private readonly Button _continueButton;

    public CompressMultiFileChoiceForm(int fileCount)
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift - Compression");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 376);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        Controls.Add(FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Compression",
            $"Compression for {fileCount} selected files",
            IconPaths.CompressVideoIcon,
            IconPaths.AppIcon,
            "▶"));

        var section = FrameShiftUiFactory.CreateFixedSection(new Point(12, 82), new Size(536, 180), "Configuration mode");
        Controls.Add(section);

        section.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(12, 20),
            Size = new Size(510, 18),
            Text = "How do you want to configure compression?",
            ForeColor = FrameShiftTheme.TextPrimary
        });

        RadioButton radioSame, radioPerFile;
        var sameTile = CreateChoiceTile(
            "Use same settings for all files",
            "One compression window, applied to all selected files.",
            new Point(12, 46), new Size(512, 56), true, out radioSame);
        section.Controls.Add(sameTile);

        var perFileTile = CreateChoiceTile(
            "Configure each file separately",
            "A compression window will open for every selected file.",
            new Point(12, 110), new Size(512, 56), false, out radioPerFile);
        section.Controls.Add(perFileTile);

        radioSame.CheckedChanged += (_, _) =>
        {
            if (!radioSame.Checked) return;
            _choice = CompressMultiFileChoice.SameForAll;
            sameTile.BackColor = FrameShiftTheme.AccentSoft;
            perFileTile.BackColor = FrameShiftTheme.Surface;
            sameTile.Invalidate();
            perFileTile.Invalidate();
        };

        radioPerFile.CheckedChanged += (_, _) =>
        {
            if (!radioPerFile.Checked) return;
            _choice = CompressMultiFileChoice.PerFile;
            perFileTile.BackColor = FrameShiftTheme.AccentSoft;
            sameTile.BackColor = FrameShiftTheme.Surface;
            sameTile.Invalidate();
            perFileTile.Invalidate();
        };

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 274), new Size(536, 44));
        Controls.Add(infoCard);
        infoCard.Controls.Add(new Label
        {
            Location = new Point(16, 11),
            Size = new Size(504, 18),
            Text = "The compressed files are created next to the originals. The format stays the same.",
            ForeColor = FrameShiftTheme.TextSecondary
        });

        _cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, 330), new Size(120, 34), false);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        _continueButton = FrameShiftUiFactory.CreateFixedActionButton("Continue", new Point(408, 330), new Size(140, 34), true);
        _continueButton.DialogResult = DialogResult.OK;
        Controls.Add(_continueButton);

        AcceptButton = _continueButton;
        CancelButton = _cancelButton;
    }

    public CompressMultiFileChoice Choice => _choice;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FrameShiftUiLayout.AutoSizeAndPositionFooterButtons(this, _cancelButton, _continueButton);
    }

    private static Panel CreateChoiceTile(string title, string description, Point location, Size size, bool selected, out RadioButton radio)
    {
        var localRadio = new RadioButton
        {
            Checked = selected,
            AutoSize = false,
            Size = new Size(18, 18),
            Location = new Point(12, 19),
            Cursor = Cursors.Hand
        };
        radio = localRadio;

        var tile = FrameShiftUiFactory.CreateFramedPanel(
            location, size,
            selected ? FrameShiftTheme.AccentSoft : FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        tile.Cursor = Cursors.Hand;
        tile.Paint += (_, e) =>
        {
            var borderColor = localRadio.Checked ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.PrimaryBlue;
            var borderWidth = localRadio.Checked ? 2F : 1F;
            FrameShiftUiPainter.DrawRoundedBorder(tile, e.Graphics, borderColor, FrameShiftUiMetrics.PanelCornerRadius, borderWidth);
        };

        localRadio.CheckedChanged += (_, _) => tile.Invalidate();
        tile.Controls.Add(localRadio);

        tile.Controls.Add(new Label
        {
            AutoSize = true,
            Location = new Point(38, 10),
            Text = title,
            ForeColor = FrameShiftTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point)
        });

        tile.Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(38, 30),
            Size = new Size(size.Width - 48, 18),
            Text = description,
            ForeColor = FrameShiftTheme.TextSecondary
        });

        tile.Click += (_, _) => localRadio.Checked = true;
        foreach (Control child in tile.Controls)
        {
            child.Click += (_, _) => localRadio.Checked = true;
            child.Cursor = Cursors.Hand;
        }

        return tile;
    }
}
