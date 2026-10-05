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

    public MainForm()
        : this(Array.Empty<string>())
    {
    }

    public MainForm(IEnumerable<string> startupPaths) : this(startupPaths, null) { }
    internal MainForm(IEnumerable<string> startupPaths, Action<ActionInvokedEventArgs>? actionHandler)
    {
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
        var settings = FrameShiftUiFactory.CreateMeasuredActionButton("Settings", false);
        settings.Click += (_, _) => { using var dialog = new SettingsForm(); dialog.ShowDialog(this); };
        var close = FrameShiftUiFactory.CreateMeasuredActionButton("Close", true);
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.Add(FrameShiftDialogLayout.CreateShell(header, body,
            FrameShiftDialogLayout.CreateActions(settings, close),
            FrameShiftUiFactory.CreateStatusMessage("Outputs are saved next to each source file · nothing is uploaded")));
        _split.BringToFront();
        _split.SizeChanged += (_, _) => SetSplitterDistance();
        _split.DpiChangedAfterParent += (_, _) => SetSplitterDistance();
        Load += (_, _) => UpdateState();
        ResumeLayout(true);
        var initial = ExpandPaths(startupPaths);
        if (initial.Count > 0)
        {
            _queuePanel.AddFiles(initial);
        }
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
        var hasFiles = _queuePanel.Items.Count > 0;
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
                $"Could not start the action:\n{ex.Message}",
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

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            _queuePanel.AddFiles(ExpandPaths(dropped));
        }
    }

    // Dropped directories are flattened one level to their immediate files; other paths pass through.
    private static IReadOnlyList<string> ExpandPaths(IEnumerable<string>? paths)
    {
        var result = new List<string>();
        if (paths is null)
        {
            return result;
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    result.AddRange(Directory.GetFiles(path));
                }
                else
                {
                    result.Add(path);
                }
            }
            catch
            {
                // Ignore unreadable paths.
            }
        }

        return result;
    }

    private Panel BuildEmptyState()
    {
        var browse = FrameShiftUiFactory.CreateMeasuredActionButton("Browse…", true);
        browse.Click += (_, _) => _queuePanel.PromptForFiles();
        return FrameShiftDialogLayout.CreateScrollBody(FrameShiftUiFactory.CreateSection("Drop files here",
            FrameShiftUiFactory.CreateVerticalStack(
                FrameShiftUiFactory.CreateWrappingLabel("Add videos, audio, images or PDFs to see the available actions."),
                FrameShiftUiFactory.CreateChoiceRow(browse))));
    }
}
