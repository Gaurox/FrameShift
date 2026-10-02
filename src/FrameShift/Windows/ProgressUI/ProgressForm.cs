using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.Logging;
using FrameShift.Core.Progress;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.ProgressUI;

public sealed partial class ProgressForm : Form, IProgressReporter
{
    private static bool s_donationBannerDismissed;

    public delegate void QueueItemRemoveRequestedEventHandler(object? sender, string queueItemId);

    private static Color PageBackgroundColor => FrameShiftTheme.PageBackground;
    private static Color SurfaceColor => FrameShiftTheme.Surface;
    private static Color DividerColor => FrameShiftTheme.SurfaceBorder;
    private static Color TitleColor => FrameShiftTheme.TextPrimary;
    private static Color BodyColor => FrameShiftTheme.TextSecondary;
    private static Color MutedColor => FrameShiftTheme.TextMuted;
    private static Color AccentColor => FrameShiftTheme.AccentText;
    private static Color DangerColor => FrameShiftTheme.ErrorText;


    private readonly Label _currentFileLabel;
    private readonly Label _currentActionLabel;
    private readonly TextBox _statusLabel;
    private readonly Label _percentLabel;
    private readonly Label _etaLabel;
    private readonly ProgressBar _progressBar;
    private readonly DataGridView _queueGrid;
    private readonly Button _cancelButton;
    private readonly Panel _donationPanel;
    private readonly Dictionary<string, DataGridViewRow> _queueRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _queueItemPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _queueItemIdsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedQueueItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _canceledQueueItems = new(StringComparer.Ordinal);
    private bool _allowProgrammaticClose;
    private bool _allowUserClose;
    private bool _closeRequested;
    private volatile bool _cancellationRequested;

    public event EventHandler? CancelRequested;
    public event QueueItemRemoveRequestedEventHandler? QueueItemRemoveRequested;

    public ProgressForm()
    {
        SuspendLayout();
        FrameShiftWindowPolicy.Initialize(this, new Size(1000, 920), new Size(380, 300));
        FrameShiftWindowChrome.Apply(this, "FrameShift - Progress");
        var header = FrameShiftUiFactory.CreateHeader("FrameShift - Progress", "No file selected",
            IconPaths.AppIcon, IconPaths.AppIcon, "▶");
        _currentActionLabel = header.TitleLabel;
        _currentFileLabel = header.SubtitleLabel;
        _statusLabel = new TextBox
        {
            Multiline = true, ReadOnly = true, WordWrap = true, AutoSize = false,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            BackColor = FrameShiftTheme.Surface, ForeColor = BodyColor,
            Text = "Waiting...", AccessibleName = "Full task or selected file details"
        };
        _statusLabel.Name = "progressStatus";
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top, Margin = Padding.Empty, Minimum = 0, Maximum = 1000,
            Style = ProgressBarStyle.Marquee, Name = "taskProgress"
        };
        _percentLabel = FrameShiftUiFactory.CreateWrappingLabel("—");
        _percentLabel.ForeColor = AccentColor;
        _percentLabel.TextAlign = ContentAlignment.MiddleRight;
        _etaLabel = FrameShiftUiFactory.CreateWrappingLabel("");
        _etaLabel.TextAlign = ContentAlignment.TopRight;
        _etaLabel.Visible = false;
        _etaLabel.TextChanged += (_, _) => _etaLabel.Visible = !string.IsNullOrWhiteSpace(_etaLabel.Text);
        var metrics = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, Margin = Padding.Empty, Size = Size.Empty
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metrics.Controls.Add(_stateLabel, 0, 0);
        metrics.Controls.Add(_percentLabel, 1, 0);
        _stateLabel.Anchor = AnchorStyles.Left;
        _stateLabel.Dock = DockStyle.None;
        var taskSection = FrameShiftUiFactory.CreateSection("Activity",
            FrameShiftUiFactory.CreateVerticalStack(metrics, _progressBar, _etaLabel));
        taskSection.BackColor = FrameShiftTheme.AccentSoft;
        Font? summaryFont = null;
        Font? percentFont = null;
        void SummaryFonts()
        {
            var oldSummary = summaryFont;
            var oldPercent = percentFont;
            summaryFont = new Font(Font, FontStyle.Bold);
            percentFont = new Font(Font.FontFamily, Font.SizeInPoints * 1.5f, FontStyle.Bold);
            _stateLabel.Font = _detailsContext.Font = summaryFont;
            _percentLabel.Font = percentFont;
            oldSummary?.Dispose();
            oldPercent?.Dispose();
        }
        FontChanged += (_, _) => SummaryFonts();
        Disposed += (_, _) => { summaryFont?.Dispose(); percentFont?.Dispose(); };
        SummaryFonts();
        _queueGrid = CreateQueueGrid();
        _queueGrid.Name = "progressQueue";
        _queueGrid.CellContentClick += QueueGridOnCellContentClick;
        _queueGrid.CellFormatting += QueueGridOnCellFormatting;
        _queueGrid.RowPrePaint += QueueGridOnRowPrePaint;
        _queueGrid.SelectionChanged += (_, _) =>
        {
            if (_queueGrid.Focused) SelectCurrentRowDetails();
        };
        _queueGrid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _queueGrid.Columns[e.ColumnIndex].Name != "Remove")
                SelectCurrentRowDetails();
        };
        var workspace = CreateDetailsWorkspace();
        _donationPanel = CreateDonationPanel();
        _donationPanel.Visible = !s_donationBannerDismissed;
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.Controls.Add(taskSection, 0, 0);
        body.Controls.Add(workspace, 0, 1);
        body.Controls.Add(_donationPanel, 0, 2);
        var viewport = FrameShiftDialogLayout.CreateScrollBody(body);
        _cancelButton = FrameShiftUiFactory.CreateMeasuredActionButton("Cancel all", false);
        _cancelButton.ForeColor = DangerColor;
        _cancelButton.Click += (_, _) =>
        {
            if (_allowUserClose) CloseSafely();
            else RequestCancel();
        };
        CancelButton = _cancelButton;
        Controls.Add(FrameShiftDialogLayout.CreateShell(header, viewport,
            FrameShiftDialogLayout.CreateActions(_cancelButton), null));
        var arranging = false;
        void LayoutBody()
        {
            if (arranging || IsDisposed) return;
            arranging = true;
            try
            {
                var gap = FrameShiftUiMetrics.ToPixels(body, FrameShiftUiMetrics.BlockGap);
                taskSection.Margin = new Padding(0, 0, 0, gap);
                _donationPanel.Margin = new Padding(0, gap, 0, 0);
                _progressBar.Height = FrameShiftUiMetrics.ToPixels(body, 12);
                var width = Math.Max(1, viewport.ClientSize.Width);
                var required = taskSection.GetPreferredSize(new Size(width, 0)).Height + gap
                    + workspace.MinimumSize.Height;
                if (!s_donationBannerDismissed)
                    required += _donationPanel.GetPreferredSize(new Size(width, 0)).Height + gap;
                body.Height = Math.Max(viewport.ClientSize.Height, required);
            }
            finally { arranging = false; }
        }
        viewport.SizeChanged += (_, _) => LayoutBody();
        body.Layout += (_, _) => LayoutBody();
        body.DpiChangedAfterParent += (_, _) => LayoutBody();
        workspace.Layout += (_, _) => LayoutBody();
        ResumeLayout(true);
        LayoutBody();
        FormClosing += (_, e) =>
        {
            AppLogger.LogStatic(
                $"ProgressForm: FormClosing entered. allowProgrammaticClose={_allowProgrammaticClose}, allowUserClose={_allowUserClose}, cancellationRequested={_cancellationRequested}, closeRequested={_closeRequested}, isDisposed={IsDisposed}, isHandleCreated={IsHandleCreated}, closeReason={e.CloseReason}.");
            if (_allowProgrammaticClose || _allowUserClose)
            {
                return;
            }

            _closeRequested = true;
            if (!_cancellationRequested)
            {
                RequestCancel();
            }

            e.Cancel = true;
        };
        FormClosed += (_, e) =>
        {
            AppLogger.LogStatic($"ProgressForm: FormClosed fired. CloseReason={e.CloseReason}, isDisposed={IsDisposed}, isHandleCreated={IsHandleCreated}.");
        };
    }

    public bool IsCancellationRequested => _cancellationRequested;

    public bool IsQueueItemRemovalRequested(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return false;
        }

        lock (_removedQueueItems)
        {
            return GetQueueItemIdsForPath(inputPath).Any(queueItemId => _removedQueueItems.Contains(queueItemId));
        }
    }

    public bool IsQueueItemRemovalRequested(string inputPath, string queueItemId)
    {
        if (string.IsNullOrWhiteSpace(queueItemId))
        {
            return IsQueueItemRemovalRequested(inputPath);
        }

        lock (_removedQueueItems)
        {
            // The row and its path mapping may already be gone from the UI. The
            // tombstone is deliberately consumed only at the execution boundary.
            return _removedQueueItems.Remove(queueItemId);
        }
    }

    public bool IsQueueItemCancellationRequested(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return false;
        }

        lock (_canceledQueueItems)
        {
            return GetQueueItemIdsForPath(inputPath).Any(queueItemId => _canceledQueueItems.Contains(queueItemId));
        }
    }

    public void RequestCancel()
    {
        AppLogger.LogStatic($"ProgressForm: RequestCancel entered. cancellationRequested={_cancellationRequested}, isDisposed={IsDisposed}, isHandleCreated={IsHandleCreated}.");
        if (_cancellationRequested)
        {
            AppLogger.LogStatic("ProgressForm: RequestCancel exited early because cancellation was already requested.");
            return;
        }

        _cancellationRequested = true;
        _closeRequested = true;
        MarkCurrentProcessingItemsAsCanceling();
        MarkQueuedItemsAsCanceled();
        UpdateStatus("Cancellation requested.");
        _cancelButton.Enabled = false;
        CancelRequested?.Invoke(this, EventArgs.Empty);
        AppLogger.LogStatic("ProgressForm: RequestCancel exited.");
    }

    public void CloseSafely()
    {
        AppLogger.LogStatic($"ProgressForm: CloseSafely called. isDisposed={IsDisposed}, isHandleCreated={IsHandleCreated}, invokeRequired={InvokeRequired}.");
        RunOnUiThread(() =>
        {
            AppLogger.LogStatic("ProgressForm: CloseSafely RunOnUiThread entered.");
            _allowProgrammaticClose = true;
            if (!IsDisposed)
            {
                AppLogger.LogStatic("ProgressForm: Close() called.");
                Close();
            }
        });
    }

    public void EnableCloseMode(string? message = null)
    {
        RunOnUiThread(() =>
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _allowUserClose = true;
            _cancelButton.Enabled = true;
            _cancelButton.Text = "Close";
            AcceptButton = _cancelButton;
            _cancelButton.ForeColor = TitleColor;
            if (!string.IsNullOrWhiteSpace(message))
            {
                ShowLiveMessage(_cancellationRequested ? "canceled" : _liveState is "failed" or "canceled" ? _liveState : "finished", message);
            }
        });
    }

    public void ReportQueue(IReadOnlyList<string> items)
    {
        RunOnUiThread(() =>
        {
            _selectedDetailId = null;
            _queueGrid.Rows.Clear();
            _queueRows.Clear();
            _queueItemPaths.Clear();
            _queueItemIdsByPath.Clear();
            lock (_removedQueueItems)
            {
                _removedQueueItems.Clear();
            }
            lock (_canceledQueueItems)
            {
                _canceledQueueItems.Clear();
            }

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                AddQueueRow(ActionQueueRunner.CreateQueueItemId(index), item, "queued", string.Empty);
            }
        });
    }

    public void AddQueueItem(string queueItemId, string item)
    {
        RunOnUiThread(() =>
        {
            if (_queueRows.ContainsKey(queueItemId))
            {
                return;
            }

            lock (_removedQueueItems)
            {
                _removedQueueItems.Remove(queueItemId);
            }
            lock (_canceledQueueItems)
            {
                _canceledQueueItems.Remove(queueItemId);
            }

            AddQueueRow(queueItemId, item, "queued", string.Empty);
        });
    }

    public void RemoveQueueItem(string queueItemId)
    {
        RunOnUiThread(() =>
        {
            RemoveQueuedItem(queueItemId);
        });
    }

    public void ReportProgress(int progressValue, string? currentFile, string? currentAction, string? message, string? etaText = null)
    {
        RunOnUiThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(currentAction))
            {
                _currentActionLabel.Text = $"FrameShift - {currentAction}";
            }

            if (!string.IsNullOrWhiteSpace(currentFile))
            {
                _currentFileLabel.Text = Path.GetFileName(currentFile);
            }

            _progressBar.Style = ProgressBarStyle.Continuous;
            var clampedValue = Math.Clamp(progressValue, 0, 1000);
            _progressBar.Value = clampedValue;

            var percent = clampedValue >= 1000
                ? "100%"
                : $"{Math.Floor(clampedValue / 10d):0}%";
            _percentLabel.Text = percent;
            var currentItemIsCanceling = !string.IsNullOrWhiteSpace(currentFile) && IsQueueItemCancellationRequested(currentFile);
            _etaLabel.Text = currentItemIsCanceling ? string.Empty : etaText ?? string.Empty;

            var liveMessage = currentItemIsCanceling ? "Canceling current file..."
                : !string.IsNullOrWhiteSpace(message) ? message
                : string.Equals(_liveFile, currentFile, StringComparison.OrdinalIgnoreCase) ? _liveMessage : "Processing...";
            ShowLiveMessage(currentItemIsCanceling ? "canceling" : "processing", liveMessage, currentFile);

            if (!string.IsNullOrWhiteSpace(currentFile) && !currentItemIsCanceling)
            {
                UpdateQueueItemInternal(currentFile, "processing", message);
            }
        });
    }

    public void ReportState(string state, string? message = null)
    {
        RunOnUiThread(() =>
        {
            ShowLiveMessage(state, string.IsNullOrWhiteSpace(message) ? state : message);
            if (state.Equals("failed", StringComparison.OrdinalIgnoreCase) || state.Equals("canceled", StringComparison.OrdinalIgnoreCase)
                || state.Equals("done", StringComparison.OrdinalIgnoreCase) || state.Equals("completed", StringComparison.OrdinalIgnoreCase))
                _progressBar.Style = ProgressBarStyle.Continuous;
            if (!string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase))
            {
                _etaLabel.Text = string.Empty;
            }
        });
    }

    public void ReportQueueItem(string currentFile, string state, string? message = null)
    {
        RunOnUiThread(() =>
        {
            UpdateQueueItemInternal(currentFile, state, message);
            if (string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase))
            {
                ResetProgressDisplay();
                if (string.IsNullOrEmpty(_liveFile) || string.Equals(_liveFile, currentFile, StringComparison.OrdinalIgnoreCase))
                    ShowLiveMessage(state, message ?? "Processing failed.", currentFile);
            }
        });
    }

    public string GetCurrentProcessingState()
    {
        if (IsDisposed || Disposing)
        {
            return "form-disposed";
        }

        try
        {
            if (InvokeRequired && IsHandleCreated)
            {
                return (string)(Invoke(new Func<string>(GetCurrentProcessingState)) ?? "unknown");
            }

            foreach (DataGridViewRow row in _queueGrid.Rows)
            {
                var state = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
                if (!string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(state, "canceling", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var queueItemId = row.Tag as string ?? string.Empty;
                var inputPath = TryGetQueueItemPath(queueItemId) ?? string.Empty;
                var details = Convert.ToString(row.Cells[2].Value) ?? string.Empty;
                return $"{Path.GetFileName(inputPath)} | state={state} | details={details}";
            }

            return "none";
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"ProgressForm: GetCurrentProcessingState failed: {ex}");
            return "diagnostic-error";
        }
    }

    public int GetQueueRowCount()
    {
        if (IsDisposed || Disposing)
        {
            return -1;
        }

        try
        {
            if (InvokeRequired && IsHandleCreated)
            {
                return (int)Invoke(new Func<int>(GetQueueRowCount));
            }

            return _queueGrid.Rows.Count;
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"ProgressForm: GetQueueRowCount failed: {ex}");
            return -1;
        }
    }

    private Panel CreateDonationPanel()
    {
        var label = FrameShiftUiFactory.CreateWrappingLabel("FrameShift is free. Support development ❤️");
        var donate = FrameShiftUiFactory.CreateMeasuredActionButton("Donate", false, 92);
        donate.Click += (_, _) => OpenDonationLink();
        var dismiss = FrameShiftUiFactory.CreateMeasuredActionButton("Close banner", false);
        dismiss.Name = "dismissDonation";
        dismiss.Click += (_, _) => DismissDonationBanner();
        return FrameShiftUiFactory.CreateChoiceRow(label, donate, dismiss);
    }
    private static DataGridView CreateQueueGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            MultiSelect = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            ColumnHeadersVisible = true,
            AccessibleName = "Task files queue",
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = SurfaceColor,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            GridColor = SurfaceColor,
            EnableHeadersVisualStyles = false
        };

        // A selected cell also selects its column header. Keep that header neutral
        // instead of inheriting the saturated Windows Highlight selection color.
        grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = BodyColor;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceColor;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = BodyColor;
        grid.DefaultCellStyle.BackColor = SurfaceColor;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.DefaultCellStyle.ForeColor = TitleColor;
        grid.DefaultCellStyle.SelectionBackColor = FrameShiftTheme.PageBackground;
        grid.DefaultCellStyle.SelectionForeColor = TitleColor;
        grid.DefaultCellStyle.Padding = new Padding(0, 6, 0, 6);

        grid.RowTemplate.Height = 40;
        grid.ScrollBars = ScrollBars.Vertical;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName", HeaderText = "File",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 45
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "State", HeaderText = "Status",
            Width = 120,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Details", Visible = false,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 55
        });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Remove", HeaderText = "",
            CellTemplate = new FrameShift.Windows.Controls.FrameShiftGridButtonCell(),
            Width = 44,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Text = "×",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleCenter
            }
        });

        FrameShiftGridUi.Apply(grid, ("State", 120), ("Remove", 44));
        return grid;
    }

    private void AddQueueRow(string queueItemId, string item, string state, string details)
    {
        var rowIndex = _queueGrid.Rows.Add(Path.GetFileName(item), state, details, "×");
        var row = _queueGrid.Rows[rowIndex];
        row.Tag = queueItemId;
        row.Cells[0].ToolTipText = item;
        row.Cells[3].ToolTipText = $"Remove or cancel {Path.GetFileName(item)}";
        _queueRows[queueItemId] = row;
        _queueItemPaths[queueItemId] = item;
        if (!_queueItemIdsByPath.TryGetValue(item, out var queueItemIds))
        {
            queueItemIds = new List<string>();
            _queueItemIdsByPath[item] = queueItemIds;
        }

        queueItemIds.Add(queueItemId);
    }

    private void UpdateQueueItemInternal(string currentFile, string state, string? message)
    {
        if (!TryGetQueueRowForPath(currentFile, state, out var queueItemId, out var row))
        {
            AppLogger.LogStatic($"ProgressForm: UpdateQueueItemInternal skipped because row was not found for '{currentFile}' with target state '{state}'.");
            return;
        }

        if (row.DataGridView != _queueGrid || row.Index < 0)
        {
            AppLogger.LogStatic($"ProgressForm: UpdateQueueItemInternal removed stale row reference for '{currentFile}' with target state '{state}'.");
            RemoveQueueRowMappings(queueItemId);
            return;
        }

        row.Cells[1].Value = state;
        row.Cells[2].Value = message ?? string.Empty;
        row.Cells[3].ReadOnly =
            !string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase);

        if (!string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(state, "canceling", StringComparison.OrdinalIgnoreCase))
        {
            lock (_canceledQueueItems)
            {
                _canceledQueueItems.Remove(queueItemId);
            }
        }

        _queueGrid.InvalidateRow(row.Index);
        if (_selectedDetailId == queueItemId) RefreshDetails();
    }

    private void QueueGridOnCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_queueGrid.Columns[e.ColumnIndex].Name != "Remove")
        {
            return;
        }

        var row = _queueGrid.Rows[e.RowIndex];
        if (row.Tag is not string queueItemId)
        {
            return;
        }

        var state = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
        if (string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase))
        {
            RemoveQueuedItem(queueItemId);
            return;
        }

        if (!string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_canceledQueueItems)
        {
            if (!_canceledQueueItems.Add(queueItemId))
            {
                return;
            }
        }

        UpdateQueueItemInternal(TryGetQueueItemPath(queueItemId) ?? string.Empty, "canceling", "Canceling current file...");
    }

    private void QueueGridOnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        var columnName = _queueGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "State")
        {
            var state = Convert.ToString(e.Value) ?? string.Empty;
            e.Value = state.ToLowerInvariant() switch
            {
                "queued" => "Waiting", "processing" => "Processing", "done" or "completed" => "Completed",
                "failed" => "Failed", "canceling" => "Canceling…", "canceled" => "Canceled", _ => state
            };
            e.FormattingApplied = true;
            if (e.CellStyle is not null)
            {
                e.CellStyle.ForeColor = GetStateColor(state);
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            }
        }

        if (columnName == "Remove")
        {
            var row = _queueGrid.Rows[e.RowIndex];
            var state = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
            var enabled =
                string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase);
            if (e.CellStyle is not null)
            {
                e.CellStyle.ForeColor = !enabled ? DividerColor
                    : string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase) ? DangerColor : MutedColor;
                e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                e.CellStyle.SelectionBackColor = _queueGrid.DefaultCellStyle.SelectionBackColor;
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }
    }

    private void QueueGridOnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        using var pen = new Pen(DividerColor);
        var rowBounds = _queueGrid.GetRowDisplayRectangle(e.RowIndex, false);
        var bounds = new Rectangle(0, rowBounds.Bottom - 1, _queueGrid.ClientSize.Width, 1);
        var inset = FrameShiftUiMetrics.ToPixels(_queueGrid, FrameShiftUiMetrics.OuterPadding);
        e.Graphics.DrawLine(pen, bounds.Left + inset, bounds.Top, bounds.Right - inset, bounds.Top);
    }

    private static Color GetStateColor(string state)
    {
        return state.ToLowerInvariant() switch
        {
            "done" or "completed" => FrameShiftTheme.SuccessText,
            "failed" => DangerColor,
            "canceled" => MutedColor,
            "canceling" => DangerColor,
            "processing" => AccentColor,
            _ => BodyColor
        };
    }

    private void UpdateStatus(string message)
    {
        RunOnUiThread(() =>
        {
            ShowLiveMessage("canceling", message);
            _etaLabel.Text = string.Empty;
        });
    }

    private void ResetProgressDisplay()
    {
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
        _percentLabel.Text = "—";
        _etaLabel.Text = string.Empty;
    }

    private void MarkCurrentProcessingItemsAsCanceling()
    {
        RunOnUiThread(() =>
        {
            foreach (DataGridViewRow row in _queueGrid.Rows)
            {
                var state = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
                if (!string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (row.Tag is not string queueItemId)
                {
                    continue;
                }

                lock (_canceledQueueItems)
                {
                    _canceledQueueItems.Add(queueItemId);
                }

                var inputPath = TryGetQueueItemPath(queueItemId);
                if (!string.IsNullOrWhiteSpace(inputPath))
                {
                    UpdateQueueItemInternal(inputPath, "canceling", "Canceling current file...");
                }
            }
        });
    }

    private void MarkQueuedItemsAsCanceled()
    {
        RunOnUiThread(() =>
        {
            foreach (DataGridViewRow row in _queueGrid.Rows)
            {
                var state = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
                if (!string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (row.Tag is not string queueItemId)
                {
                    continue;
                }

                var inputPath = TryGetQueueItemPath(queueItemId);
                if (!string.IsNullOrWhiteSpace(inputPath))
                {
                    UpdateQueueItemInternal(inputPath, "canceled", MediaActionMessages.CanceledBeforeProcessing());
                }
            }
        });
    }

    private IEnumerable<string> GetQueueItemIdsForPath(string inputPath)
    {
        return _queueItemIdsByPath.TryGetValue(inputPath, out var queueItemIds)
            ? queueItemIds.ToArray()
            : Array.Empty<string>();
    }

    private string? TryGetQueueItemPath(string queueItemId)
    {
        return _queueItemPaths.TryGetValue(queueItemId, out var inputPath)
            ? inputPath
            : null;
    }

    private void RemoveQueueRowMappings(string queueItemId)
    {
        _queueRows.Remove(queueItemId);
        if (!_queueItemPaths.Remove(queueItemId, out var inputPath) || string.IsNullOrWhiteSpace(inputPath))
        {
            return;
        }

        if (!_queueItemIdsByPath.TryGetValue(inputPath, out var queueItemIds))
        {
            return;
        }

        queueItemIds.Remove(queueItemId);
        if (queueItemIds.Count == 0)
        {
            _queueItemIdsByPath.Remove(inputPath);
        }
    }

    private void RemoveQueuedItem(string queueItemId)
    {
        if (!_queueRows.TryGetValue(queueItemId, out var row))
        {
            return;
        }

        if (_selectedDetailId == queueItemId) _selectedDetailId = null;
        _queueGrid.Rows.Remove(row);
        RemoveQueueRowMappings(queueItemId);
        lock (_removedQueueItems)
        {
            _removedQueueItems.Add(queueItemId);
        }

        RefreshDetails();
        QueueItemRemoveRequested?.Invoke(this, queueItemId);
    }

    private bool TryGetQueueRowForPath(string inputPath, string targetState, out string queueItemId, out DataGridViewRow row)
    {
        if (!_queueItemIdsByPath.TryGetValue(inputPath, out var queueItemIds) || queueItemIds.Count == 0)
        {
            queueItemId = string.Empty;
            row = null!;
            return false;
        }

        (string QueueItemId, DataGridViewRow? Row) FindRow(Func<string, bool> predicate)
        {
            foreach (var candidateQueueItemId in queueItemIds)
            {
                if (!_queueRows.TryGetValue(candidateQueueItemId, out var candidateRow))
                {
                    continue;
                }

                var candidateState = Convert.ToString(candidateRow.Cells[1].Value) ?? string.Empty;
                if (!predicate(candidateState))
                {
                    continue;
                }

                return (candidateQueueItemId, candidateRow);
            }

            return (string.Empty, null);
        }

        var match = FindRow(state =>
                string.Equals(state, "canceling", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "processing", StringComparison.OrdinalIgnoreCase));

        if (match.Row is null && string.Equals(targetState, "processing", StringComparison.OrdinalIgnoreCase))
        {
            match = FindRow(state => string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase));
        }

        if (match.Row is null)
        {
            match = FindRow(state => string.Equals(state, "queued", StringComparison.OrdinalIgnoreCase));
        }

        if (match.Row is null)
        {
            match = FindRow(_ => true);
        }

        queueItemId = match.QueueItemId;
        if (match.Row is null)
        {
            row = null!;
            return false;
        }

        row = match.Row;
        return true;
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
        {
            AppLogger.LogStatic("ProgressForm: RunOnUiThread skipped because form is disposing/disposed.");
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                if (!IsHandleCreated)
                {
                    AppLogger.LogStatic("ProgressForm: RunOnUiThread skipped BeginInvoke because handle is not created.");
                    return;
                }

                BeginInvoke(action);
            }
            catch (Exception ex)
            {
                AppLogger.LogStatic($"ProgressForm: RunOnUiThread BeginInvoke exception: {ex}");
            }
        }
        else
        {
            if (IsDisposed || Disposing)
            {
                AppLogger.LogStatic("ProgressForm: RunOnUiThread direct path skipped because form is disposing/disposed.");
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLogger.LogStatic($"ProgressForm: RunOnUiThread direct exception: {ex}");
                if (!(_closeRequested || IsDisposed || Disposing))
                {
                    throw;
                }
            }
        }
    }

    private void DismissDonationBanner()
    {
        s_donationBannerDismissed = true;
        _donationPanel.Visible = false;

        PerformLayout();
    }

    private void OpenDonationLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://paypal.me/gaurox")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogStatic($"ProgressForm: failed to open donate link. {ex}");
        }
    }
}
