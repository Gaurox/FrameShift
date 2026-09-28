using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FrameShift.Core.AI.CreateSubtitles;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

internal sealed class CreateSubtitlesPickerForm : Form
{
    private string _selectedModelId;
    private CreateSubtitlesOutputFormat _selectedOutputFormat;
    private CreateSubtitlesAssPreset _selectedAssPreset;
    private readonly Panel _assPresetSection;
    private readonly TableLayoutPanel _layout;
    private bool _loaded;

    public CreateSubtitlesPickerForm(
        string actionTitle,
        string sourceLabel,
        CreateSubtitlesOutputFormat initialOutputFormat = CreateSubtitlesOutputFormat.StandardSrt,
        CreateSubtitlesAssPreset initialAssPreset = CreateSubtitlesAssPreset.Classic)
    {
        _selectedModelId = CreateSubtitlesModelCatalog.GetDefault().Id;
        _selectedOutputFormat = initialOutputFormat;
        _selectedAssPreset = initialAssPreset;
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(680, 640), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, $"FrameShift - {actionTitle}", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader($"FrameShift - {actionTitle}", $"Source: {sourceLabel}",
            IconPaths.CreateSubtitlesAiIcon, IconPaths.FrameShiftAiIcon, "AI");
        var modelControls = new List<Control>();
        foreach (var model in CreateSubtitlesModelCatalog.GetAll())
        {
            var radio = CreateRadio(model.DisplayName, BuildRowDescription(model));
            radio.Checked = model.Id == _selectedModelId;
            radio.Tag = model.Id;
            radio.CheckedChanged += (_, _) => { if (radio.Checked) _selectedModelId = model.Id; };
            modelControls.Add(radio);
        }
        var outputControls = new List<Control>();
        foreach (var format in CreateSubtitlesOutputFormats.GetAll())
        {
            var radio = CreateRadio(format.GetDisplayName(), format.GetDescription());
            radio.Checked = format == _selectedOutputFormat;
            radio.Tag = format;
            radio.CheckedChanged += (_, _) =>
            {
                if (!radio.Checked) return;
                _selectedOutputFormat = format;
                UpdateAssPresetVisibility();
            };
            outputControls.Add(radio);
        }
        var presetControls = new List<Control>();
        foreach (var preset in CreateSubtitlesAssPresets.GetAll())
        {
            var radio = CreateRadio(preset.GetDisplayName(), preset.GetDescription());
            radio.Checked = preset == _selectedAssPreset;
            radio.Tag = preset;
            radio.CheckedChanged += (_, _) => { if (radio.Checked) _selectedAssPreset = preset; };
            presetControls.Add(radio);
        }
        _assPresetSection = FrameShiftUiFactory.CreateSection("ASS style", CreateRadioList(presetControls));
        _assPresetSection.Name = "assPresets";
        var content = FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateSection("Transcription model", CreateRadioList(modelControls)),
            FrameShiftUiFactory.CreateSection("Output format", CreateRadioList(outputControls)), _assPresetSection);
        var cancel = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancel.DialogResult = DialogResult.Cancel;
        var create = FrameShiftUiFactory.CreateMeasuredActionButton("Create File", true);
        create.DialogResult = DialogResult.OK;
        _layout = FrameShiftDialogLayout.Create(header, content, FrameShiftDialogLayout.CreateActions(cancel, create),
            FrameShiftUiFactory.CreateStatusMessage("Transcription runs locally. Creates a new subtitle file next to the source."));
        Controls.Add(_layout);
        Load += (_, _) =>
        {
            _loaded = true;
            UpdateAssPresetVisibility();
        };
        AcceptButton = create;
        CancelButton = cancel;
        UpdateAssPresetVisibility();
        ResumeLayout(true);
    }

    public string SelectedModelId => _selectedModelId;

    private static RadioButton CreateRadio(string title, string description) => new()
    {
        Text = title, AccessibleDescription = description, AutoSize = true,
        Dock = DockStyle.Top, Margin = Padding.Empty,
        ForeColor = FrameShiftTheme.TextPrimary, UseVisualStyleBackColor = true
    };

    private static TableLayoutPanel CreateRadioList(List<Control> choices)
    {
        // All radios share one parent: native exclusivity and arrow navigation are preserved.
        var list = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty, Size = Size.Empty
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var choice in choices)
        {
            var row = list.RowCount;
            list.RowCount += 2;
            list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list.Controls.Add(choice, 0, row);
            var description = FrameShiftUiFactory.CreateWrappingLabel(choice.AccessibleDescription ?? "");
            description.Click += (_, _) => { ((RadioButton)choice).Checked = true; choice.Focus(); };
            list.Controls.Add(description, 0, row + 1);
        }
        void Metrics()
        {
            foreach (Control control in list.Controls)
            {
                var row = list.GetRow(control);
                control.Margin = new Padding(row % 2 == 1 ? FrameShiftUiMetrics.ToPixels(list, 22) : 0, 0, 0,
                    row % 2 == 1 && row < list.RowCount - 1
                        ? FrameShiftUiMetrics.ToPixels(list, FrameShiftUiMetrics.BlockGap) : 0);
            }
        }
        list.HandleCreated += (_, _) => Metrics();
        list.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        return list;
    }

    public CreateSubtitlesOutputFormat SelectedOutputFormat => _selectedOutputFormat;

    public CreateSubtitlesAssPreset SelectedAssPreset => _selectedAssPreset;

    internal bool IsAssPresetSectionVisible => _selectedOutputFormat == CreateSubtitlesOutputFormat.AdvancedAss;

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return Path.GetFileName(inputPaths[0]);
        }

        return $"{inputPaths.Count} selected files";
    }

    private static string BuildRowDescription(CreateSubtitlesModelDefinition model)
    {
        var size = FormatDownloadSize(model.ExpectedTotalSizeBytes);
        return model.Id switch
        {
            "whisper-base" => $"{size} · CPU only · Good for testing and short clips",
            "whisper-small" => $"{size} · CPU only · Best balance of speed and accuracy",
            "whisper-turbo" => $"{size} · CPU only · Highest accuracy — allow extra processing time on CPU",
            _ => $"{size} · CPU only"
        };
    }

    private static string FormatDownloadSize(long bytes)
    {
        const double gb = 1_073_741_824d;
        const double mb = 1_048_576d;
        return bytes >= gb
            ? $"~{bytes / gb:F1} GB"
            : $"~{(long)Math.Round(bytes / mb)} MB";
    }

    private void UpdateAssPresetVisibility()
    {
        if (_assPresetSection is not null)
            _assPresetSection.Visible = _selectedOutputFormat == CreateSubtitlesOutputFormat.AdvancedAss;
        if (_loaded && WindowState == FormWindowState.Normal)
        {
            // Fit only on opening or an explicit format choice, never while the user resizes.
            // Hidden ASS options must not leave a reserved empty area in SRT/project mode.
            var workingArea = Screen.FromControl(this).WorkingArea;
            FrameShiftDialogLayout.FitInitialHeight(this, _layout, workingArea.Size);
            Bounds = FrameShiftWindowPolicy.FitBounds(Bounds, workingArea);
        }
    }
}
