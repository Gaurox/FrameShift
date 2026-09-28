using System.Drawing;
using System.Windows.Forms;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.ProgressUI;

public sealed partial class ProgressForm
{
    private readonly Label _stateLabel = FrameShiftUiFactory.CreateWrappingLabel("Waiting for a task");
    private readonly Label _detailsContext = FrameShiftUiFactory.CreateWrappingLabel("Current task · Waiting");
    private readonly Button _followButton = FrameShiftUiFactory.CreateMeasuredActionButton("Current task", false);
    private string _liveState = "waiting";
    private string _liveMessage = "Waiting...";
    private string _liveFile = "";
    private string? _selectedDetailId;

    private TableLayoutPanel CreateDetailsWorkspace()
    {
        _stateLabel.Name = "progressSummary";
        _detailsContext.Name = "detailsContext";
        var copy = FrameShiftUiFactory.CreateMeasuredActionButton("Copy details", false);
        copy.Name = "copyProgressDetails";
        copy.Click += (_, _) => { if (_statusLabel.TextLength > 0) Clipboard.SetText(_statusLabel.Text); };
        _followButton.Name = "followCurrentTask";
        _followButton.Enabled = false;
        _followButton.Click += (_, _) => { _selectedDetailId = null; RefreshDetails(); };
        _detailsContext.Dock = DockStyle.None;
        _detailsContext.TextAlign = ContentAlignment.MiddleLeft;
        var toolbar = FrameShiftUiFactory.CreateChoiceRow(_detailsContext, copy, _followButton);
        var detailsBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        detailsBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailsBody.Controls.Add(toolbar, 0, 0);
        detailsBody.Controls.Add(_statusLabel, 0, 1);
        var details = FrameShiftUiFactory.CreateSection("Messages & details", detailsBody, fill: true);
        var queueBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        queueBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        queueBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        queueBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        queueBody.Controls.Add(_queueGrid, 0, 0);
        var hint = FrameShiftUiFactory.CreateWrappingLabel("Select a file to read its message below. × removes or cancels it.");
        queueBody.Controls.Add(hint, 0, 1);
        var queue = FrameShiftUiFactory.CreateSection("Files", queueBody, fill: true);
        var queueTitle = (Label)queue.GetControlFromPosition(0, 0)!;
        void UpdateCount() => queueTitle.Text = $"Files · {_queueGrid.Rows.Count}";
        _queueGrid.RowsAdded += (_, _) => UpdateCount();
        _queueGrid.RowsRemoved += (_, _) => UpdateCount();
        UpdateCount();
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Name = "progressWorkspace",
            ColumnCount = 1, RowCount = 2
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        workspace.Controls.Add(queue, 0, 0);
        workspace.Controls.Add(details, 0, 1);
        var arranging = false;
        void Arrange()
        {
            if (arranging) return;
            arranging = true;
            try
            {
                var gap = FrameShiftUiMetrics.ToPixels(workspace, FrameShiftUiMetrics.BlockGap);
                queue.Margin = new Padding(0, 0, 0, gap);
                _queueGrid.Margin = details.Margin = toolbar.Margin = Padding.Empty;
                _detailsContext.MinimumSize = new Size(0, copy.GetPreferredSize(Size.Empty).Height);
                _statusLabel.Margin = Padding.Empty;
                hint.Margin = new Padding(0, gap, 0, 0);
                var width = Math.Max(1, workspace.ClientSize.Width - details.Padding.Horizontal);
                // Reserve a full-width reading area with at least six lines, including under text enlargement. The outer body
                // scrolls if necessary; this editor itself always fills its allocated area.
                var readingHeight = Math.Max(FrameShiftUiMetrics.ToPixels(workspace, 110), _statusLabel.Font.Height * 6);
                var detailsMinimum = readingHeight + toolbar.GetPreferredSize(new Size(width, 0)).Height
                    + details.Padding.Vertical + FrameShiftUiMetrics.ToPixels(workspace, FrameShiftUiMetrics.SectionContentGap)
                    + details.GetControlFromPosition(0, 0)!.GetPreferredSize(new Size(width, 0)).Height;
                var queueMinimum = _queueGrid.ColumnHeadersHeight + _queueGrid.RowTemplate.Height * 3
                    + hint.GetPreferredSize(new Size(width, 0)).Height + queue.Padding.Vertical + 3 * gap
                    + queueTitle.GetPreferredSize(new Size(width, 0)).Height;
                var minimum = detailsMinimum + queueMinimum;
                workspace.MinimumSize = new Size(0, minimum);
                workspace.RowStyles[1].Height = Math.Max(detailsMinimum, (int)Math.Round(workspace.ClientSize.Height * 0.3));
            }
            finally { arranging = false; }
        }
        workspace.Layout += (_, _) => Arrange();
        workspace.DpiChangedAfterParent += (_, _) => Arrange();
        workspace.FontChanged += (_, _) => Arrange();
        Arrange();
        return workspace;
    }

    private void ShowLiveMessage(string state, string message, string? file = null)
    {
        _liveState = state;
        _liveMessage = message;
        if (!string.IsNullOrWhiteSpace(file))
        {
            _liveFile = file;
            _currentFileLabel.Text = Path.GetFileName(file);
        }
        _stateLabel.Text = DescribeState(state);
        _stateLabel.ForeColor = GetStateColor(state);
        RefreshDetails();
    }

    private void SelectCurrentRowDetails()
    {
        if (_queueGrid.CurrentRow?.Tag is not string id) return;
        _selectedDetailId = id;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        var state = _liveState;
        var message = _liveMessage;
        var file = _liveFile;
        if (_selectedDetailId is not null && _queueRows.TryGetValue(_selectedDetailId, out var row))
        {
            state = Convert.ToString(row.Cells[1].Value) ?? "queued";
            message = Convert.ToString(row.Cells[2].Value) ?? "";
            file = TryGetQueueItemPath(_selectedDetailId) ?? "";
            if (string.IsNullOrWhiteSpace(message)) message = DescribeState(state);
        }
        else _selectedDetailId = null;
        _followButton.Enabled = _selectedDetailId is not null;
        _detailsContext.ForeColor = GetStateColor(state);
        _detailsContext.Text = $"{(_selectedDetailId is null ? "Current task" : "Selected file")} · {DescribeState(state)}";
        var text = string.IsNullOrEmpty(file) ? message : file + Environment.NewLine + Environment.NewLine + message;
        // Identical progress ticks must not disturb an error being read or selected.
        if (_statusLabel.Text == text) return;
        _statusLabel.Text = text;
        _statusLabel.Select(0, 0);
        _statusLabel.ScrollToCaret();
    }

    private static string DescribeState(string state) => state.ToLowerInvariant() switch
    {
        "waiting" => "Waiting for a task", "queued" => "Waiting in queue", "processing" => "Processing",
        "done" or "completed" => "Completed", "failed" => "Failed — see details",
        "canceling" => "Canceling…", "canceled" => "Canceled", "finished" => "Finished",
        _ => state
    };
}
