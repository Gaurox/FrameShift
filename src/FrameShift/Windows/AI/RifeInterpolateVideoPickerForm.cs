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
        FrameShiftWindowChrome.Apply(this, L.Get("RifeInterpolateVideoPickerForm_FrameShift_Interpolate_Video_RIFE_b31ddc2a"), IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader(L.Get("RifeInterpolateVideoPickerForm_FrameShift_Interpolate_Video_RIFE_b31ddc2a"),
            L.Format("RifeInterpolateVideoPickerForm_Source_0_1_fps_4ac163bc",Path.GetFileName(inputPath),sourceFps.ToString("0.###", CultureInfo.InvariantCulture)),
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
        _keepPitchCheckBox = new CheckBox { Name = "keepPitch", Text = L.Get("RifeInterpolateVideoPickerForm_Keep_original_pitch_cc942898"), AutoSize = true, Checked = true };
        _removeAudioCheckBox = new CheckBox { Name = "removeAudio", Text = L.Get("RifeInterpolateVideoPickerForm_Remove_audio_in_slow_motion_c901886b"), AutoSize = true };
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
        var settings = FrameShiftUiFactory.CreateSection(L.Get("RifeInterpolateVideoPickerForm_Interpolation_ff88c81d"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow(L.Get("RifeInterpolateVideoPickerForm_Model_a53a5cc6"), _modelSelector),
            FrameShiftUiFactory.CreateFieldRow(L.Get("RifeInterpolateVideoPickerForm_Source_FPS_662c5c77"), _sourceFpsTextBox, logicalEditorWidth: 128),
            FrameShiftUiFactory.CreateFieldRow(L.Get("RifeInterpolateVideoPickerForm_Target_multiplier_7b606242"), _targetSelector, logicalEditorWidth: 128),
            FrameShiftUiFactory.CreateFieldRow(L.Get("RifeInterpolateVideoPickerForm_Playback_55249c0b"), _speedSelector, logicalEditorWidth: 240),
            FrameShiftUiFactory.CreateFieldRow(L.Get("RifeInterpolateVideoPickerForm_Output_FPS_7df5c998"), _outputFpsTextBox, logicalEditorWidth: 128)));
        var audio = FrameShiftUiFactory.CreateSection(L.Get("RifeInterpolateVideoPickerForm_Audio_2fd1cbe8"), FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_keepPitchCheckBox), FrameShiftUiFactory.CreateChoiceRow(_removeAudioCheckBox)));
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RifeInterpolateVideoPickerForm_Cancel_998b9c48"), false);
        cancel.DialogResult = DialogResult.Cancel;
        var start = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("RifeInterpolateVideoPickerForm_Start_7d734349"), true);
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
            ? L.Get("RifeInterpolateVideoPickerForm_Audio_is_copied_in_Normal_Speed_mode_5ab3feee")
            : _removeAudioCheckBox.Checked
                ? L.Get("RifeInterpolateVideoPickerForm_Audio_is_removed_in_Slow_Motion_mode_fb0bfed1")
                : _keepPitchCheckBox.Checked
                    ? L.Get("RifeInterpolateVideoPickerForm_Audio_is_slowed_down_and_original_pi_b5f77d97")
                    : L.Get("RifeInterpolateVideoPickerForm_Audio_is_slowed_down_and_pitch_is_lo_03d20ac1");
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
        public static readonly SpeedModeOption Normal = new(L.Get("RifeInterpolateVideoPickerForm_Normal_Speed_2dfb3d6e"), 1);
        public static readonly SpeedModeOption[] All = [Normal, new(L.Get("RifeInterpolateVideoPickerForm_Slow_Motion_x2_5f8960d6"), 2), new(L.Get("RifeInterpolateVideoPickerForm_Slow_Motion_x4_2814515e"), 4)];
    }
}