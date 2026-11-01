using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

/// <summary>
/// Drop-driven hub: a file queue on the left and the actions applicable to what is
/// queued/selected on the right. Files arrive via drag-and-drop, "Add…", or a launch
/// selection. Clicking an action launches a child FrameShift.exe on the whole scope
/// (no 15-file Explorer cap). Shows a drop zone while the queue is empty.
/// </summary>
public sealed class MainForm : Form
{

    private readonly FileQueuePanel _queuePanel;
    private readonly ActionsPanel _actionsPanel;
    private readonly SplitContainer _split;
    private readonly Panel _emptyState;
    private readonly string[] _startupPaths;
    private Task<int>? _initialImport;
    private bool _closing;
    private bool _allowClose;

    public MainForm()
        : this(Array.Empty<string>())
    {
    }

    public MainForm(IEnumerable<string> startupPaths) : this(startupPaths, null) { }
    internal MainForm(IEnumerable<string> startupPaths, Action<ActionInvokedEventArgs>? actionHandler)
    {
        _startupPaths = startupPaths?.ToArray() ?? [];
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(1000, 680), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift");
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = FrameShiftTheme.PageBackground;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        _queuePanel = new FileQueuePanel { Dock = DockStyle.Fill };
        _queuePanel.QueueChanged += (_, _) => OnQueueChanged();
        _queuePanel.ImportStateChanged += (_, _) => UpdateState();
        _queuePanel.SelectionChanged += (_, _) => RefreshActions();

        _actionsPanel = new ActionsPanel { Dock = DockStyle.Fill };
        _actionsPanel.ActionInvoked += actionHandler is null ? OnActionInvoked : (_, e) => actionHandler(e);

        // Note: Panel minima start at zero to allow layout before the control is realized.
        // Setting large min sizes before the control is realized throws during layout
        // ("SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize").
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = FrameShiftTheme.SurfaceBorder,
            SplitterWidth = 10,
            Panel1MinSize = 0, Panel2MinSize = 0,
            Visible = false
        };
        _split.Panel1.BackColor = FrameShiftTheme.Surface;
        _split.Panel2.BackColor = FrameShiftTheme.Surface;
        _split.Panel1.Controls.Add(_queuePanel);
        _split.Panel2.Controls.Add(_actionsPanel);

        _emptyState = BuildEmptyState();

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = FrameShiftTheme.PageBackground,
            Margin = Padding.Empty
        };
        body.Controls.Add(_split);
        body.Controls.Add(_emptyState);

        var header = FrameShiftUiFactory.CreateHeader("FrameShift", $"v{Application.ProductVersion}",
            IconPaths.AppIcon, IconPaths.AppIcon, "F");
        var settings = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("MainForm_Settings_8d4d0d85"), false);
        settings.Click += (_, _) => { using var dialog = new SettingsForm(); dialog.ShowDialog(this); };
        var close = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("MainForm_Close_6cf4a777"), true);
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.Add(FrameShiftDialogLayout.CreateShell(header, body,
            FrameShiftDialogLayout.CreateActions(settings, close),
            FrameShiftUiFactory.CreateStatusMessage(L.Get("MainForm_Outputs_are_saved_next_to_each_sourc_097da8ad"))));
        _split.BringToFront();
        _split.SizeChanged += (_, _) => SetSplitterDistance();
        _split.DpiChangedAfterParent += (_, _) => SetSplitterDistance();
        Load += (_, _) => UpdateState();
        Shown += async (_, _) => await StartInitialPathsAsync();
        FormClosing += CloseAfterImportsAsync;
        ResumeLayout(true);
    }

    internal Task<int> StartInitialPathsAsync() => _initialImport ??= _queuePanel.AddPathsAsync(_startupPaths);

    private async void CloseAfterImportsAsync(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.Cancel) return;
        var pending = _queuePanel.StopPathImportsAsync();
        if (pending.IsCompleted) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        Enabled = false;
        await pending;
        if (IsDisposed) return;
        BeginInvoke(new Action(() => { if (IsDisposed) return; _allowClose = true; Close(); }));
    }

    private void OnQueueChanged()
    {
        UpdateState();
        RefreshActions();
    }

    private void RefreshActions()
    {
        _actionsPanel.SetFiles(_queuePanel.Items, _queuePanel.SelectedPaths);
    }

    private void UpdateState()
    {
        var hasFiles = _queuePanel.Items.Count > 0 || _queuePanel.HasImportStatus;
        _split.Visible = hasFiles;
        _emptyState.Visible = !hasFiles;

        if (hasFiles)
        {
            SetSplitterDistance();
        }
    }

    private bool _arrangingSplit;
    private void SetSplitterDistance()
    {
        if (_arrangingSplit) return;
        _arrangingSplit = true;
        try
        {
            _split.Orientation = _split.Width < FrameShiftUiMetrics.ToPixels(this, 640)
                ? Orientation.Horizontal : Orientation.Vertical;
            var extent = _split.Orientation == Orientation.Vertical ? _split.Width : _split.Height;
            if (extent < 20) return;
            _split.SplitterWidth = Math.Min(extent / 4, FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.BlockGap));
            _split.SplitterDistance = Math.Clamp((int)(extent * 0.4), 0, extent - _split.SplitterWidth);
        }
        finally { _arrangingSplit = false; }
    }
    private void OnActionInvoked(object? sender, ActionInvokedEventArgs e)
    {
        try
        {
            ActionLauncher.Launch(e.Entry, e.Files);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                L.Format("MainForm_Could_not_start_the_action_0_d8a4b165",ex.Message),
                "FrameShift",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            await _queuePanel.AddPathsAsync(dropped);
        }
    }

    private Panel BuildEmptyState()
    {
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton(L.Get("MainForm_Browse_2a9dd9af"), true);
        browse.Click += (_, _) => _queuePanel.PromptForFiles();
        return FrameShiftDialogLayout.CreateScrollBody(FrameShiftUiFactory.CreateSection(L.Get("MainForm_Drop_files_here_6bb980c3"),
            FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateWrappingLabel(L.Get("MainForm_Add_videos_audio_images_or_PDFs_to_s_925e7d26")),
                FrameShiftUiFactory.CreateChoiceRow(browse))));
    }
}
