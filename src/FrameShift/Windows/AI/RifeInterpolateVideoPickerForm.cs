using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public sealed class RifeInterpolateVideoPickerForm : Form
{
    private readonly double _sourceFps;
    private readonly TextBox _sourceFpsTextBox;
    private readonly TextBox _outputFpsTextBox;
    private readonly Label _infoLabel;
    private readonly Button _cancelButton;
    private readonly Button _startButton;
    private readonly CheckBox _keepPitchCheckBox;
    private readonly CheckBox _removeAudioCheckBox;
    private readonly Label _modelSelectorLabel;
    private readonly Label _targetSelectorLabel;
    private readonly Label _speedSelectorLabel;
    private readonly ContextMenuStrip _modelMenu;
    private readonly ContextMenuStrip _targetMenu;
    private readonly ContextMenuStrip _speedMenu;
    private readonly Panel _modelSelectorPanel;
    private readonly Panel _targetSelectorPanel;
    private readonly Panel _speedSelectorPanel;
    private RifeModelDefinition _selectedModel;
    private int _selectedMultiplier;
    private SpeedModeOption _selectedSpeedMode;

    public RifeInterpolateVideoPickerForm(string inputPath, double sourceFps)
    {
        _sourceFps = sourceFps;
        _selectedModel = RifeModelCatalog.GetDefault();
        _selectedMultiplier = _selectedModel.SupportedMultipliers[0];
        _selectedSpeedMode = SpeedModeOption.Normal;
        _modelMenu = CreateSelectorMenu();
        _targetMenu = CreateSelectorMenu();
        _speedMenu = CreateSelectorMenu();

        FrameShiftWindowChrome.Apply(this, "FrameShift - Interpolate Video (RIFE)", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 514);
        BackColor = FrameShiftTheme.PageBackground;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var header = FrameShiftUiFactory.CreateFixedHeader(
            "FrameShift - Interpolate Video (RIFE)",
            $"Source: {Path.GetFileName(inputPath)}  —  {sourceFps.ToString("0.###", CultureInfo.InvariantCulture)} fps",
            IconPaths.InterpolateAiIcon,
            IconPaths.FrameShiftAiIcon,
            "AI");
        Controls.Add(header);

        var sourceSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 82), new Size(536, 56), "Source");
        Controls.Add(sourceSection);

        sourceSection.Controls.Add(new Label
        {
            Location = new Point(18, 28),
            Size = new Size(500, 18),
            Text = $"Detected frame rate: {sourceFps.ToString("0.###", CultureInfo.InvariantCulture)} fps",
            ForeColor = FrameShiftTheme.TextSecondary
        });

        var settingsSection = FrameShiftUiFactory.CreateFixedSection(
            new Point(12, 150), new Size(536, 212), "RIFE");
        Controls.Add(settingsSection);

        settingsSection.Controls.Add(CreateFieldLabel("Source FPS", new Point(18, 30), 88));
        settingsSection.Controls.Add(CreateFieldLabel("Target", new Point(118, 30), 88));
        settingsSection.Controls.Add(CreateFieldLabel("Output FPS", new Point(218, 30), 100));
        settingsSection.Controls.Add(CreateFieldLabel("Playback", new Point(330, 30), 188));

        _sourceFpsTextBox = FrameShiftUiFactory.CreateValueTextBox(readOnly: true);
        _sourceFpsTextBox.Text = sourceFps.ToString("0.###", CultureInfo.InvariantCulture);
        settingsSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_sourceFpsTextBox, new Point(18, 50), new Size(88, 30)));

        _targetSelectorPanel = CreateSelectorPanel(new Point(118, 50), new Size(88, 30), out _targetSelectorLabel, _targetMenu);
        settingsSection.Controls.Add(_targetSelectorPanel);

        _outputFpsTextBox = FrameShiftUiFactory.CreateValueTextBox(readOnly: true);
        settingsSection.Controls.Add(FrameShiftUiFactory.CreateFixedTextInputHost(_outputFpsTextBox, new Point(218, 50), new Size(100, 30)));

        _speedSelectorPanel = CreateSelectorPanel(new Point(330, 50), new Size(188, 30), out _speedSelectorLabel, _speedMenu);
        settingsSection.Controls.Add(_speedSelectorPanel);

        settingsSection.Controls.Add(CreateFieldLabel("Model", new Point(18, 90), 88));

        _modelSelectorPanel = CreateSelectorPanel(new Point(18, 110), new Size(500, 30), out _modelSelectorLabel, _modelMenu);
        settingsSection.Controls.Add(_modelSelectorPanel);

        _keepPitchCheckBox = new CheckBox
        {
            Location = new Point(18, 154),
            Size = new Size(220, 22),
            Text = "Keep original pitch",
            ForeColor = FrameShiftTheme.TextPrimary,
            Checked = true
        };
        _keepPitchCheckBox.CheckedChanged += (_, _) => RefreshCalculatedValues();
        settingsSection.Controls.Add(_keepPitchCheckBox);

        _removeAudioCheckBox = new CheckBox
        {
            Location = new Point(18, 182),
            Size = new Size(230, 22),
            Text = "Remove audio in slow motion",
            ForeColor = FrameShiftTheme.TextPrimary,
            Checked = false
        };
        _removeAudioCheckBox.CheckedChanged += (_, _) => RefreshCalculatedValues();
        settingsSection.Controls.Add(_removeAudioCheckBox);

        var infoCard = FrameShiftUiFactory.CreateFixedInfoCard(new Point(12, 374), new Size(536, 44));
        Controls.Add(infoCard);

        _infoLabel = new Label
        {
            Location = new Point(16, 11),
            Size = new Size(504, 18),
            ForeColor = FrameShiftTheme.TextSecondary,
            AutoEllipsis = true
        };
        infoCard.Controls.Add(_infoLabel);

        _cancelButton = FrameShiftUiFactory.CreateFixedActionButton("Cancel", new Point(278, 468), new Size(120, 34), primary: false);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(_cancelButton);

        _startButton = FrameShiftUiFactory.CreateFixedActionButton("Start", new Point(408, 468), new Size(120, 34), primary: true);
        _startButton.Click += OnStartClicked;
        Controls.Add(_startButton);

        AcceptButton = _startButton;
        CancelButton = _cancelButton;

        RebuildModelMenu();
        RebuildSpeedMenu();
        RefreshTargets();
    }

    public RifeInterpolateVideoSettings? Selection { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FrameShiftUiLayout.AutoSizeAndPositionFooterButtons(this, _cancelButton, _startButton);
    }

    private void RefreshTargets()
    {
        if (Array.IndexOf(_selectedModel.SupportedMultipliers, _selectedMultiplier) < 0)
        {
            _selectedMultiplier = _selectedModel.SupportedMultipliers[0];
        }

        RebuildTargetMenu();
        _modelSelectorLabel.Text = _selectedModel.DisplayName;
        _targetSelectorLabel.Text = $"x{_selectedMultiplier}";
        RefreshCalculatedValues();
    }

    private void RefreshCalculatedValues()
    {
        var targetFps = _sourceFps * _selectedMultiplier / _selectedSpeedMode.PlaybackDivisor;
        var isSlowMotion = _selectedSpeedMode.PlaybackDivisor > 1;
        _removeAudioCheckBox.Enabled = isSlowMotion;
        if (!isSlowMotion)
        {
            _removeAudioCheckBox.Checked = false;
        }

        _keepPitchCheckBox.Enabled = isSlowMotion && !_removeAudioCheckBox.Checked;
        if (!isSlowMotion)
        {
            _keepPitchCheckBox.Checked = true;
        }

        _outputFpsTextBox.Text = targetFps.ToString("0.###", CultureInfo.InvariantCulture);
        _speedSelectorLabel.Text = _selectedSpeedMode.Label;
        _infoLabel.Text = !isSlowMotion
            ? "Audio is copied in Normal Speed mode."
            : _removeAudioCheckBox.Checked
                ? "Audio is removed in Slow Motion mode."
                : _keepPitchCheckBox.Checked
                    ? "Audio is slowed down and original pitch is preserved."
                    : "Audio is slowed down and pitch is lowered.";
    }

    private void OnStartClicked(object? sender, EventArgs e)
    {
        Selection = new RifeInterpolateVideoSettings(
            _selectedModel.Id,
            _selectedMultiplier,
            _selectedSpeedMode.PlaybackDivisor,
            _removeAudioCheckBox.Checked,
            _keepPitchCheckBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RebuildModelMenu()
    {
        _modelMenu.Items.Clear();
        foreach (var model in RifeModelCatalog.GetAll())
        {
            var localModel = model;
            var item = new ToolStripMenuItem(localModel.DisplayName)
            {
                Checked = string.Equals(localModel.Id, _selectedModel.Id, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                _selectedModel = localModel;
                RebuildModelMenu();
                RefreshTargets();
            };
            _modelMenu.Items.Add(item);
        }
    }

    private void RebuildTargetMenu()
    {
        _targetMenu.Items.Clear();
        foreach (var multiplier in _selectedModel.SupportedMultipliers)
        {
            var localMultiplier = multiplier;
            var item = new ToolStripMenuItem($"x{localMultiplier}")
            {
                Checked = localMultiplier == _selectedMultiplier
            };
            item.Click += (_, _) =>
            {
                _selectedMultiplier = localMultiplier;
                RebuildTargetMenu();
                RefreshCalculatedValues();
            };
            _targetMenu.Items.Add(item);
        }
    }

    private void RebuildSpeedMenu()
    {
        _speedMenu.Items.Clear();
        foreach (var option in SpeedModeOption.All)
        {
            var localOption = option;
            var item = new ToolStripMenuItem(localOption.Label)
            {
                Checked = localOption.PlaybackDivisor == _selectedSpeedMode.PlaybackDivisor
            };
            item.Click += (_, _) =>
            {
                _selectedSpeedMode = localOption;
                RebuildSpeedMenu();
                RefreshCalculatedValues();
            };
            _speedMenu.Items.Add(item);
        }
    }

    private static Label CreateFieldLabel(string text, Point location, int width)
    {
        return new Label
        {
            Location = location,
            Size = new Size(width, 16),
            Text = text,
            ForeColor = FrameShiftTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static ContextMenuStrip CreateSelectorMenu()
    {
        return new ContextMenuStrip
        {
            AutoSize = false,
            ShowImageMargin = false,
            RenderMode = ToolStripRenderMode.System,
            Width = 160
        };
    }

    private static Panel CreateSelectorPanel(Point location, Size size, out Label valueLabel, ContextMenuStrip menu)
    {
        var panel = FrameShiftUiFactory.CreateFramedPanel(
            location,
            size,
            FrameShiftTheme.Surface,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.InputCornerRadius);
        panel.Padding = new Padding(8, 5, 8, 5);
        panel.Cursor = Cursors.Hand;
        panel.Click += (_, _) => ShowSizedMenu(panel, menu);

        valueLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand
        };
        valueLabel.Click += (_, _) => ShowSizedMenu(panel, menu);
        panel.Controls.Add(valueLabel);

        var arrowLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Right,
            Width = 16,
            Text = "▾",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = FrameShiftTheme.TextSecondary,
            Cursor = Cursors.Hand
        };
        arrowLabel.Click += (_, _) => ShowSizedMenu(panel, menu);
        panel.Controls.Add(arrowLabel);

        return panel;
    }

    private static void ShowSizedMenu(Control anchor, ContextMenuStrip menu)
    {
        var menuWidth = Math.Max(anchor.Width, 160);
        menu.Width = menuWidth;

        foreach (ToolStripItem item in menu.Items)
        {
            item.AutoSize = false;
            item.Width = Math.Max(0, menuWidth - 2);
        }

        var preferredHeight = menu.GetPreferredSize(new Size(menuWidth, int.MaxValue)).Height;
        menu.Size = new Size(menuWidth, preferredHeight);

        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private sealed record SpeedModeOption(string Label, int PlaybackDivisor)
    {
        public static readonly SpeedModeOption Normal = new("Normal Speed", 1);
        public static readonly SpeedModeOption SlowX2 = new("Slow Motion x2", 2);
        public static readonly SpeedModeOption SlowX4 = new("Slow Motion x4", 4);

        public static readonly SpeedModeOption[] All =
        [
            Normal,
            SlowX2,
            SlowX4
        ];
    }
}
