using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.AI.Upscale;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.AI;

/// <summary>
/// Compact model + scale picker shown before the Upscale Image action runs. Uses the standard
/// FrameShift styled dropdown for the model, plus a scale row (x2 / x3 / x4 / custom size). The
/// custom width/height fields keep the source aspect ratio locked (editing one updates the other).
/// </summary>
public class UpscaleImagePickerForm : Form
{
    private readonly ComboBox _modelSelector;
    private readonly Label _descriptionLabel;
    private string _selectedModelId;

    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly double _ratio;
    private readonly bool _allowCustom;
    private bool _updatingFields;
    private readonly bool _videoMode;

    private readonly RadioButton _scale2;
    private readonly RadioButton _scale3;
    private readonly RadioButton _scale4;
    private readonly RadioButton _scaleCustom;
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;

    public UpscaleImagePickerForm(
        string sourceLabel,
        int sourceWidth,
        int sourceHeight,
        bool allowCustomSize,
        string? initialModelId = null,
        bool videoMode = false)
    {
        _videoMode = videoMode;
        var models = videoMode ? UpscaleModelCatalog.GetVideoModels() : UpscaleModelCatalog.GetImageModels();
        var requestedInitial = UpscaleModelCatalog.GetById(initialModelId);
        var initial = requestedInitial is not null && models.Any(model => model.Id == requestedInitial.Id)
            ? requestedInitial
            : videoMode ? UpscaleModelCatalog.GetDefaultVideo() : UpscaleModelCatalog.GetDefault();
        _selectedModelId = initial.Id;

        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _allowCustom = allowCustomSize && sourceWidth > 0 && sourceHeight > 0;
        _ratio = _allowCustom ? (double)sourceWidth / sourceHeight : 1d;

        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(640, 500), new Size(400, 300));
        string actionTitle = videoMode ? "Upscale Video" : "Upscale Image";
        string actionIcon = videoMode ? IconPaths.UpscaleVideoAiIcon : IconPaths.UpscaleImageAiIcon;
        FrameShiftWindowChrome.Apply(this, $"FrameShift - {actionTitle}", IconPaths.FrameShiftAiIcon, IconPaths.AppIcon);
        var header = FrameShiftUiFactory.CreateHeader($"FrameShift - {actionTitle}", $"Source: {sourceLabel}",
            actionIcon, IconPaths.FrameShiftAiIcon, "AI");
        _modelSelector = new ComboBox { Name = "model", DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "DisplayName" };
        foreach (var model in models) _modelSelector.Items.Add(model);
        _descriptionLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _modelSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_modelSelector.SelectedItem is UpscaleModelDefinition model) SelectModel(model.Id);
        };
        _modelSelector.SelectedItem = initial;
        var modelSection = FrameShiftUiFactory.CreateSection("Model", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateFieldRow("&Model", _modelSelector), _descriptionLabel));
        _scale2 = CreateScaleRadio("2x");
        _scale3 = CreateScaleRadio("3x");
        _scale4 = CreateScaleRadio("4x");
        _scaleCustom = CreateScaleRadio("Custom size");
        _scale4.Checked = true;
        _scaleCustom.Enabled = _allowCustom;
        _widthBox = new TextBox { Name = "width", TextAlign = HorizontalAlignment.Right };
        _heightBox = new TextBox { Name = "height", TextAlign = HorizontalAlignment.Right };
        var scaleSection = FrameShiftUiFactory.CreateSection("Scale", FrameShiftUiFactory.CreateVerticalStack(
            FrameShiftUiFactory.CreateChoiceRow(_scale2, _scale3, _scale4, _scaleCustom),
            FrameShiftUiFactory.CreateFieldRow("&Width", _widthBox, "px", 128),
            FrameShiftUiFactory.CreateFieldRow("&Height", _heightBox, "px", 128),
            FrameShiftUiFactory.CreateWrappingLabel(_allowCustom
                ? $"Aspect locked to the source. Maximum {_sourceWidth * 4} × {_sourceHeight * 4} px (x4)."
                : $"Custom size needs a single {(videoMode ? "video" : "image")}; x2 / x3 / x4 still apply to every selected file.")));
        _scale2.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _scale3.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _scale4.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _scaleCustom.CheckedChanged += (_, _) => RefreshCustomEnabled();
        _widthBox.TextChanged += (_, _) => OnWidthTyped();
        _heightBox.TextChanged += (_, _) => OnHeightTyped();
        RefreshCustomEnabled();
        var cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel", false);
        cancelButton.DialogResult = DialogResult.Cancel;
        var upscaleButton = FrameShiftUiFactory.CreateMeasuredActionButton("Upscale", true);
        upscaleButton.DialogResult = DialogResult.OK;
        AcceptButton = upscaleButton;
        CancelButton = cancelButton;
        var status = FrameShiftUiFactory.CreateStatusMessage(videoMode
            ? "The upscaled video keeps its frame rate and audio when supported. The AI model is downloaded once if it is not already installed."
            : "The upscaled image is saved as a new PNG next to the source. The AI model is downloaded once if it is not already installed.");
        var root = FrameShiftDialogLayout.Create(header, FrameShiftUiFactory.CreateVerticalStack(modelSection, scaleSection),
            FrameShiftDialogLayout.CreateActions(cancelButton, upscaleButton), status);
        Controls.Add(root);
        Load += (_, _) => FrameShiftDialogLayout.FitInitialHeight(this, root);
        ResumeLayout(true);
    }
    /// <summary>The chosen model id, or null when the dialog was cancelled.</summary>
    public string? SelectedModelId => DialogResult == DialogResult.OK ? _selectedModelId : null;

    /// <summary>Preset factor ("2", "3", "4") when a preset is chosen; null when custom size is chosen.</summary>
    public string? SelectedScale
    {
        get
        {
            if (_scale2.Checked) return "2";
            if (_scale3.Checked) return "3";
            if (_scale4.Checked) return "4";
            return null;
        }
    }

    /// <summary>The custom target size (aspect-locked, clamped to x1..x4), or null when not in custom mode.</summary>
    public (int Width, int Height)? CustomTarget
    {
        get
        {
            if (!_scaleCustom.Checked || !_allowCustom) return null;

            if (TryParsePositiveInt(_widthBox.Text) is int w)
            {
                double factor = Math.Clamp((double)w / _sourceWidth, 1d, 4d);
                return (Round(_sourceWidth * factor), Round(_sourceHeight * factor));
            }

            if (TryParsePositiveInt(_heightBox.Text) is int h)
            {
                double factor = Math.Clamp((double)h / _sourceHeight, 1d, 4d);
                return (Round(_sourceWidth * factor), Round(_sourceHeight * factor));
            }

            return null;
        }
    }

    public static string BuildSourceLabel(IReadOnlyList<string> inputPaths)
    {
        if (inputPaths.Count <= 1)
        {
            return Path.GetFileName(inputPaths[0]);
        }

        return $"{inputPaths.Count} selected files";
    }

    private void RefreshCustomEnabled()
    {
        bool on = _scaleCustom.Checked && _allowCustom;
        _widthBox.Enabled = on;
        _heightBox.Enabled = on;
        _widthBox.ForeColor = on ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;
        _heightBox.ForeColor = on ? FrameShiftTheme.TextPrimary : FrameShiftTheme.TextMuted;

        if (on && string.IsNullOrWhiteSpace(_widthBox.Text))
        {
            _updatingFields = true;
            _widthBox.Text = (_sourceWidth * 4).ToString(CultureInfo.InvariantCulture);
            _heightBox.Text = (_sourceHeight * 4).ToString(CultureInfo.InvariantCulture);
            _updatingFields = false;
        }
    }

    private void OnWidthTyped()
    {
        if (_updatingFields || !_scaleCustom.Checked) return;
        if (TryParsePositiveInt(_widthBox.Text) is not int w) return;

        _updatingFields = true;
        _heightBox.Text = Math.Max(1, (int)Math.Round(w / _ratio)).ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;
    }

    private void OnHeightTyped()
    {
        if (_updatingFields || !_scaleCustom.Checked) return;
        if (TryParsePositiveInt(_heightBox.Text) is not int h) return;

        _updatingFields = true;
        _widthBox.Text = Math.Max(1, (int)Math.Round(h * _ratio)).ToString(CultureInfo.InvariantCulture);
        _updatingFields = false;
    }

    private void SelectModel(string modelId)
    {
        var model = UpscaleModelCatalog.GetById(modelId) ??
            (_videoMode ? UpscaleModelCatalog.GetDefaultVideo() : UpscaleModelCatalog.GetDefault());
        _selectedModelId = model.Id;
        _descriptionLabel.Text = model.Summary;
    }

    private static int Round(double value) => Math.Max(1, (int)Math.Round(value));

    private static int? TryParsePositiveInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    private static RadioButton CreateScaleRadio(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = FrameShiftTheme.TextPrimary, UseVisualStyleBackColor = true
    };
}