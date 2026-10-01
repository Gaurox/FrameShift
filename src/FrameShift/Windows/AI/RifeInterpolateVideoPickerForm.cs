using System.Drawing;
using System.Globalization;
using FrameShift.Core.AI.VideoInterpolation;
using FrameShift.Core.Actions;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

public sealed class RifeInterpolateVideoPickerForm : Form
{
    private readonly double _sourceFps;
    private readonly TextBox _sourceFpsTextBox;
    private readonly TextBox _outputFpsTextBox;
    private readonly FrameShiftStatusMessage _infoLabel;
    private readonly CheckBox _keepPitchCheckBox;
    private readonly CheckBox _removeAudioCheckBox;
    private readonly ComboBox _modelSelector;
    private readonly ComboBox _targetSelector;
    private readonly ComboBox _speedSelector;
    private RifeModelDefinition _selectedModel;
    private int _selectedMultiplier;
    private SpeedModeOption _selectedSpeedMode = SpeedModeOption.Normal;
    private bool _updatingTargets;

    public RifeInterpolateVideoPickerForm(string inputPath, double sourceFps)
    {
        _sourceFps = sourceFps;
        _selectedModel = RifeModelCatalog.GetDefault();
        _selectedMultiplier = _selectedModel.SupportedMultipliers[0];
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 520), new Size(400, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Interpolate Video (RIFE)", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Interpolate Video (RIFE)",
            $"Source: {Path.GetFileName(inputPath)} — {sourceFps.ToString("0.###", CultureInfo.InvariantCulture)} fps",
            IconPaths.InterpolateAiIcon, IconPaths.FrameShiftAiIcon, "AI");
        _sourceFpsTextBox = new TextBox { Name = "sourceFps", ReadOnly = true, Text = sourceFps.ToString("0.###", CultureInfo.InvariantCulture) };
        _outputFpsTextBox = new TextBox { Name = "outputFps", ReadOnly = true };
        _modelSelector = Selector("model", "DisplayName");
        foreach (var model in RifeModelCatalog.GetAll()) _modelSelector.Items.Add(model);
        _modelSelector.SelectedItem = _selectedModel;
        _targetSelector = Selector("target");
        _speedSelector = Selector("playback", "Label");
        _speedSelector.Items.AddRange(SpeedModeOption.All);
        _speedSelector.SelectedItem = _selectedSpeedMode;
        _keepPitchCheckBox = new CheckBox { Name = "keepPitch", Text = "Keep original pitch", AutoSize = true, Checked = true };
        _removeAudioCheckBox = new CheckBox { Name = "removeAudio", Text = "Remove audio in slow motion", AutoSize = true };
        _infoLabel = FrameShiftUiFactory.CreateStatusMessage("");
        _modelSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_modelSelector.SelectedItem is RifeModelDefinition model)
            {
                _selectedModel = model;
                RefreshTargets();
            }
        };
        _targetSelector.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingTargets && _targetSelector.SelectedItem is int multiplier)
            {
                _selectedMultiplier = multiplier;
                RefreshCalculatedValues();
            }
        };
        _speedSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_speedSelector.SelectedItem is SpeedModeOption mode)
            {
                _selectedSpeedMode = mode;
                RefreshCalculatedValues();
            }
        };
        _keepPitchCheckBox.CheckedChanged += (_, _) => RefreshCalculatedValues();
        _removeAudioCheckBox.CheckedChanged += (_, _) => RefreshCalculatedValues();
        var settings = FrameShiftUiFactory.CreateSection("Interpolation", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("&Model", _modelSelector),
            FrameShiftUiFactory.CreateFieldRow("Source FPS", _sourceFpsTextBox, logicalEditorWidth: 128),
            FrameShiftUiFactory.CreateFieldRow("&Target multiplier", _targetSelector, logicalEditorWidth: 128),
            FrameShiftUiFactory.CreateFieldRow("&Playback", _speedSelector, logicalEditorWidth: 240),
            FrameShiftUiFactory.CreateFieldRow("Output FPS", _outputFpsTextBox, logicalEditorWidth: 128)));
        var audio = FrameShiftUiFactory.CreateSection("Audio", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_keepPitchCheckBox), FrameShiftUiFactory.CreateChoiceRow(_removeAudioCheckBox)));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var start = FrameShiftUiFactory.CreateMeasuredActionButton("Start", true);
        start.Click += OnStartClicked;
        AcceptButton = start;
        CancelButton = cancel;
        var root = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(settings, audio),
            FrameShiftDialogLayout.CreateActions(cancel, start), _infoLabel);
        Controls.Add(root);
        RefreshTargets();
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
        ResumeLayout(true);
    }

    public RifeInterpolateVideoSettings? Selection { get; private set; }

    private void RefreshTargets()
    {
        if (Array.IndexOf(_selectedModel.SupportedMultipliers, _selectedMultiplier) < 0)
            _selectedMultiplier = _selectedModel.SupportedMultipliers[0];
        _updatingTargets = true;
        try
        {
            _targetSelector.Items.Clear();
            foreach (var multiplier in _selectedModel.SupportedMultipliers) _targetSelector.Items.Add(multiplier);
            _targetSelector.SelectedItem = _selectedMultiplier;
        }
        finally { _updatingTargets = false; }
        RefreshCalculatedValues();
    }

    private void RefreshCalculatedValues()
    {
        var targetFps = _sourceFps * _selectedMultiplier / _selectedSpeedMode.PlaybackDivisor;
        var isSlowMotion = _selectedSpeedMode.PlaybackDivisor > 1;
        _removeAudioCheckBox.Enabled = isSlowMotion;
        if (!isSlowMotion) _removeAudioCheckBox.Checked = false;
        _keepPitchCheckBox.Enabled = isSlowMotion && !_removeAudioCheckBox.Checked;
        if (!isSlowMotion) _keepPitchCheckBox.Checked = true;
        _outputFpsTextBox.Text = targetFps.ToString("0.###", CultureInfo.InvariantCulture);
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
        Selection = new RifeInterpolateVideoSettings(_selectedModel.Id, _selectedMultiplier,
            _selectedSpeedMode.PlaybackDivisor, _removeAudioCheckBox.Checked, _keepPitchCheckBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static ComboBox Selector(string name, string displayMember = "")
        => new() { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = displayMember };

    private sealed record SpeedModeOption(string Label, int PlaybackDivisor)
    {
        public static readonly SpeedModeOption Normal = new("Normal Speed", 1);
        public static readonly SpeedModeOption[] All = [Normal, new("Slow Motion x2", 2), new("Slow Motion x4", 4)];
    }
}