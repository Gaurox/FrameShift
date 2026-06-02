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

public sealed class ProgressForm : Form, IProgressReporter
{
    private static bool s_donationBannerDismissed;

    public delegate void QueueItemRemoveRequestedEventHandler(object? sender, string queueItemId);

    private static readonly Color PageBackgroundColor = FrameShiftTheme.PageBackground;
    private static readonly Color SurfaceColor = FrameShiftTheme.Surface;
    private static readonly Color DividerColor = FrameShiftTheme.SurfaceBorder;
    private static readonly Color TitleColor = FrameShiftTheme.TextPrimary;
    private static readonly Color BodyColor = FrameShiftTheme.TextSecondary;
    private static readonly Color MutedColor = FrameShiftTheme.TextMuted;
    private static readonly Color AccentColor = FrameShiftTheme.SecondaryBlue;
    private static readonly Color DangerColor = Color.FromArgb(198, 40, 40);

    private static readonly Font s_font9 = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_fontSemibold9 = new("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_fontSemibold11 = new("Segoe UI Semibold", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_fontSemibold16 = new("Segoe UI Semibold", 16F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_font10 = new("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

    private readonly Label _eyebrowLabel;
    private readonly Label _currentFileLabel;
    private readonly Label _currentActionLabel;
    private readonly Label _statusLabel;
    private readonly Label _queueTitleLabel;
    private readonly Label _queueHintLabel;
    private readonly Label _percentLabel;
    private readonly Label _etaLabel;
    private readonly ProgressBar _progressBar;
    private readonly DataGridView _queueGrid;
    private readonly Button _cancelButton;
    private readonly Panel _donationPanel;
    private readonly RowStyle _donationRowStyle;
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
        Text = "FrameShift Progress";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 560);
        Size = new Size(1060, 640);
        Font = s_font9;
        BackColor = PageBackgroundColor;

        if (File.Exists(IconPaths.AppIcon))
        {
            Icon = new Icon(IconPaths.AppIcon);
        }

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PageBackgroundColor,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 4
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 188F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _donationRowStyle = new RowStyle(SizeType.Absolute, s_donationBannerDismissed ? 0F : 48F);
        rootLayout.RowStyles.Add(_donationRowStyle);
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));

        var headerPanel = CreateSurfacePanel(new Padding(24, 20, 24, 20));

        _eyebrowLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            ForeColor = AccentColor,
            Font = s_fontSemibold9,
            Text = "Current task"
        };

        _currentActionLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = TitleColor,
            Font = s_fontSemibold16,
            Text = "No active task"
        };

        _currentFileLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = BodyColor,
            Font = s_font10,
            AutoEllipsis = true,
            Text = "No file selected"
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = BodyColor,
            Text = "Waiting..."
        };

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 10,
            Margin = new Padding(0, 12, 0, 0),
            Minimum = 0,
            Maximum = 1000,
            Style = ProgressBarStyle.Continuous
        };

        _percentLabel = new Label
        {
            Dock = DockStyle.Left,
            Width = 80,
            ForeColor = TitleColor,
            Font = s_fontSemibold9,
            Text = "0%"
        };

        _etaLabel = new Label
        {
            Dock = DockStyle.Right,
            Width = 280,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = MutedColor,
            Text = string.Empty
        };

        var metricsPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(0, 10, 0, 0)
        };
        metricsPanel.Controls.Add(_etaLabel);
        metricsPanel.Controls.Add(_percentLabel);

        var headerFlow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = SurfaceColor
        };
        headerFlow.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        headerFlow.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        headerFlow.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        headerFlow.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        headerFlow.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        headerFlow.Controls.Add(_eyebrowLabel, 0, 0);
        headerFlow.Controls.Add(_currentActionLabel, 0, 1);
        headerFlow.Controls.Add(_currentFileLabel, 0, 2);
        headerFlow.Controls.Add(_statusLabel, 0, 3);

        var progressStack = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceColor
        };
        progressStack.Controls.Add(metricsPanel);
        progressStack.Controls.Add(_progressBar);
        headerFlow.Controls.Add(progressStack, 0, 4);
        headerPanel.Controls.Add(headerFlow);

        var queuePanel = CreateSurfacePanel(new Padding(0));

        _queueTitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = TitleColor,
            Font = s_fontSemibold11,
            Text = "Queue"
        };

        _queueHintLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = MutedColor,
            Text = "Click the red cross to remove a queued file before it starts."
        };

        _queueGrid = CreateQueueGrid();
        _queueGrid.CellContentClick += QueueGridOnCellContentClick;
        _queueGrid.CellFormatting += QueueGridOnCellFormatting;
        _queueGrid.RowPrePaint += QueueGridOnRowPrePaint;

        var queueHeaderPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 66,
            Padding = new Padding(24, 18, 24, 8),
            BackColor = SurfaceColor
        };
        queueHeaderPanel.Controls.Add(_queueHintLabel);
        queueHeaderPanel.Controls.Add(_queueTitleLabel);

        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = DividerColor
        };

        queuePanel.Controls.Add(_queueGrid);
        queuePanel.Controls.Add(divider);
        queuePanel.Controls.Add(queueHeaderPanel);

        _donationPanel = CreateDonationPanel();
        _donationPanel.Visible = !s_donationBannerDismissed;

        _cancelButton = FrameShiftUiFactory.CreateActionButton("Cancel all", primary: false, width: 120);
        _cancelButton.Height = 38;
        _cancelButton.ForeColor = DangerColor;
        _cancelButton.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        _cancelButton.FlatAppearance.MouseDownBackColor = FrameShiftTheme.AccentSoftHover;
        _cancelButton.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        _cancelButton.Click += (_, _) =>
        {
            if (_allowUserClose)
            {
                CloseSafely();
                return;
            }

            RequestCancel();
        };

        var footerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PageBackgroundColor
        };
        footerPanel.Controls.Add(_cancelButton);
        footerPanel.SizeChanged += (_, _) =>
        {
            _cancelButton.Location = new Point(footerPanel.ClientSize.Width - _cancelButton.Width, 16);
        };

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(queuePanel, 0, 1);
        rootLayout.Controls.Add(_donationPanel, 0, 2);
        rootLayout.Controls.Add(footerPanel, 0, 3);
        Controls.Add(rootLayout);

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
            _allowUserClose = true;
            _cancelButton.Enabled = true;
            _cancelButton.Text = "Close";
            _cancelButton.ForeColor = TitleColor;
            if (!string.IsNullOrWhiteSpace(message))
            {
                _statusLabel.Text = message;
            }
        });
    }

    public void ReportQueue(IReadOnlyList<string> items)
    {
        RunOnUiThread(() =>
        {
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
                AddQueueRow(CreateAnonymousQueueItemId(item, index), item, "queued", string.Empty);
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
            if (!_queueRows.TryGetValue(queueItemId, out var row))
            {
                return;
            }

            _queueGrid.Rows.Remove(row);
            RemoveQueueRowMappings(queueItemId);
        });
    }

    public void ReportProgress(int progressValue, string? currentFile, string? currentAction, string? message, string? etaText = null)
    {
        RunOnUiThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(currentAction))
            {
                _currentActionLabel.Text = currentAction;
            }

            if (!string.IsNullOrWhiteSpace(currentFile))
            {
                _currentFileLabel.Text = Path.GetFileName(currentFile);
            }

            var clampedValue = Math.Clamp(progressValue, 0, 1000);
            _progressBar.Value = clampedValue;

            var percent = clampedValue >= 1000
                ? "100%"
                : $"{Math.Floor(clampedValue / 10d):0}%";
            _percentLabel.Text = percent;
            var currentItemIsCanceling = !string.IsNullOrWhiteSpace(currentFile) && IsQueueItemCancellationRequested(currentFile);
            _etaLabel.Text = currentItemIsCanceling ? string.Empty : etaText ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(message))
            {
                _statusLabel.Text = currentItemIsCanceling
                    ? "Canceling current file..."
                    : AppendEta(message, etaText);
            }

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
            _statusLabel.Text = string.IsNullOrWhiteSpace(message) ? state : message;
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

    private static Panel CreateSurfacePanel(Padding padding)
    {
        var panel = FrameShiftUiFactory.CreateFramedPanel(
            SurfaceColor,
            FrameShiftTheme.PrimaryBlue,
            FrameShiftUiMetrics.PanelCornerRadius);
        panel.Dock = DockStyle.Fill;
        panel.Padding = padding;
        return panel;
    }

    private Panel CreateDonationPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PageBackgroundColor,
            Margin = new Padding(0)
        };

        var separator = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = DividerColor
        };

        var label = new Label
        {
            Text = "FrameShift is free. Support development ❤️",
            ForeColor = MutedColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0)
        };

        var donateButton = FrameShiftUiFactory.CreateActionButton("Donate", primary: false, width: 92);
        donateButton.Height = 30;
        donateButton.Click += (_, _) => OpenDonationLink();

        var closeBannerButton = new Button
        {
            Text = "Close banner",
            FlatStyle = FlatStyle.Flat,
            ForeColor = MutedColor,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Width = 112,
            Height = 30,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Location = new Point(0, 9)
        };
        closeBannerButton.FlatAppearance.BorderSize = 0;
        closeBannerButton.FlatAppearance.MouseOverBackColor = Color.Transparent;
        closeBannerButton.Click += (_, _) => DismissDonationBanner();

        var leftFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0)
        };
        leftFlow.Controls.Add(label);
        leftFlow.Controls.Add(donateButton);

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0)
        };
        contentPanel.Controls.Add(leftFlow);
        contentPanel.Controls.Add(closeBannerButton);
        contentPanel.SizeChanged += (_, _) =>
        {
            closeBannerButton.Left = contentPanel.ClientSize.Width - closeBannerButton.Width;
            leftFlow.Top = Math.Max(0, (contentPanel.ClientSize.Height - leftFlow.Height) / 2);
        };

        panel.Controls.Add(contentPanel);
        panel.Controls.Add(separator);
        return panel;
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
            ColumnHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = SurfaceColor,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            GridColor = SurfaceColor,
            EnableHeadersVisualStyles = false
        };

        grid.DefaultCellStyle.BackColor = SurfaceColor;
        grid.DefaultCellStyle.ForeColor = BodyColor;
        grid.DefaultCellStyle.SelectionBackColor = FrameShiftTheme.AccentSoft;
        grid.DefaultCellStyle.SelectionForeColor = TitleColor;
        grid.DefaultCellStyle.Padding = new Padding(0, 6, 0, 6);
        grid.DefaultCellStyle.Font = s_font9;
        grid.RowTemplate.Height = 40;
        grid.ScrollBars = ScrollBars.Vertical;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 45
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "State",
            Width = 120,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Details",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 55
        });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Remove",
            Width = 44,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Text = "×",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat
        });

        return grid;
    }

    private void AddQueueRow(string queueItemId, string item, string state, string details)
    {
        var rowIndex = _queueGrid.Rows.Add(Path.GetFileName(item), state, details, "×");
        var row = _queueGrid.Rows[rowIndex];
        row.Tag = queueItemId;
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
            _queueGrid.Rows.RemoveAt(e.RowIndex);
            RemoveQueueRowMappings(queueItemId);
            lock (_removedQueueItems)
            {
                _removedQueueItems.Add(queueItemId);
            }

            QueueItemRemoveRequested?.Invoke(this, queueItemId);
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
            if (e.CellStyle is not null)
            {
                e.CellStyle.ForeColor = GetStateColor(state);
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
                e.CellStyle.ForeColor = enabled ? DangerColor : DividerColor;
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
        e.Graphics.DrawLine(pen, bounds.Left + 24, bounds.Top, bounds.Right - 24, bounds.Top);
    }

    private static Color GetStateColor(string state)
    {
        return state.ToLowerInvariant() switch
        {
            "done" => Color.FromArgb(28, 116, 70),
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
            _statusLabel.Text = message;
            _etaLabel.Text = string.Empty;
        });
    }

    private void ResetProgressDisplay()
    {
        _progressBar.Value = 0;
        _percentLabel.Text = "0%";
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

    private static string CreateAnonymousQueueItemId(string inputPath, int index)
    {
        return $"anon::{index}::{inputPath}";
    }

    private static string AppendEta(string message, string? etaText)
    {
        if (string.IsNullOrWhiteSpace(etaText))
        {
            return message;
        }

        return $"{message} | {etaText}";
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
        _donationRowStyle.Height = 0F;
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
