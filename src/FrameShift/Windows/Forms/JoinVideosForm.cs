using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

internal sealed class JoinVideosForm : Form
{
    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v"];
    private static readonly string[] SortOrderKeys = ["AsReceived", "NaturalName", "DateCreated", "DateModified"];
    private const int DefaultSortOrderIndex = 1;

    private readonly List<JoinVideoTimelineItem> _items = [];
    private readonly FfmpegRunner _ffmpegRunner;
    private readonly FfprobeRunner _ffprobeRunner;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly CancellationTokenSource _loadCancellationSource = new();
    private readonly JoinVideosTimelineControl _timeline = new();
    private readonly Panel _timelineViewport = new() { Dock = DockStyle.Fill, BackColor = FrameShiftTheme.PageBackground };
    private readonly ComboBox _orderComboBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private readonly Button _clearAllButton;
    private readonly Button _joinButton;
    private readonly Func<IWin32Window, string[]?> _videoPicker;
    private readonly object _pendingPathsSync = new();
    private readonly List<string> _pendingPaths = [];
    private readonly System.Windows.Forms.Timer _incomingPathsTimer = new() { Interval = 50 };
    private readonly string? _uiSettingsPathForTesting;
    private int _nextReceivedIndex;
    private int _pendingExternalDropIndex = -1;
    private bool _loading;
    private bool _disposed;

    public JoinVideosForm(
        IReadOnlyList<string> initialPaths,
        string ffmpegPath,
        string ffprobePath,
        FfmpegRunner ffmpegRunner,
        FfprobeRunner ffprobeRunner,
        Func<IWin32Window, string[]?>? videoPicker = null,
        string? uiSettingsPathForTesting = null)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _ffmpegRunner = ffmpegRunner;
        _ffprobeRunner = ffprobeRunner;
        _videoPicker = videoPicker ?? ShowVideoPicker;
        _uiSettingsPathForTesting = uiSettingsPathForTesting;

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = FrameShiftTheme.PageBackground;
        ClientSize = new Size(960, 600);
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        AllowDrop = true;
        KeyPreview = true;
        FrameShiftWindowChrome.Apply(this, "FrameShift - Join Videos");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = FrameShiftTheme.PageBackground,
            Padding = new Padding(FrameShiftUiMetrics.OuterPadding),
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.HeaderHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.BlockGap));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, FrameShiftUiMetrics.CompactFooterHeight));

        root.Controls.Add(
            FrameShiftUiFactory.CreateFillHeader(
                "FrameShift - Join Videos",
                "Arrange clips on one track. Explorer may not preserve the order in which files were selected.",
                IconPaths.ContextMenuIco("join-videos-video-icon.ico"),
                IconPaths.AppIcon,
                "J",
                780),
            0,
            0);

        var timelineSection = FrameShiftUiFactory.CreateFillSection("Timeline", out var timelineHost);
        timelineHost.Padding = Padding.Empty;
        var toolbar = CreateTimelineToolbar(out _addButton, out _removeButton, out _clearAllButton);
        _timelineViewport.Controls.Add(_timeline);
        var timelineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 2
        };
        timelineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        timelineLayout.Controls.Add(toolbar, 0, 0);
        timelineLayout.Controls.Add(_timelineViewport, 0, 1);
        timelineHost.Controls.Add(timelineLayout);

        var infoCard = FrameShiftUiFactory.CreateFillInfoCard();
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = FrameShiftTheme.TextSecondary;
        _statusLabel.AutoEllipsis = true;
        infoCard.Controls.Add(_statusLabel);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = FrameShiftTheme.PageBackground };
        var cancelButton = FrameShiftUiFactory.CreateActionButton("Cancel", primary: false, FrameShiftUiMetrics.SecondaryButtonWidth);
        _joinButton = FrameShiftUiFactory.CreateActionButton("Join videos", primary: true, FrameShiftUiMetrics.PrimaryButtonWidth);
        _joinButton.Enabled = false;
        _joinButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _joinButton.Location = new Point(footer.Width - _joinButton.Width, 8);
        cancelButton.Location = new Point(_joinButton.Left - FrameShiftUiMetrics.FooterButtonGap - cancelButton.Width, 8);
        footer.Resize += (_, _) =>
        {
            _joinButton.Location = new Point(footer.ClientSize.Width - _joinButton.Width, 8);
            cancelButton.Location = new Point(_joinButton.Left - FrameShiftUiMetrics.FooterButtonGap - cancelButton.Width, 8);
        };
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(_joinButton);

        root.Controls.Add(timelineSection, 0, 2);
        root.Controls.Add(infoCard, 0, 3);
        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);

        _timeline.SelectedIndexChanged += (_, _) => UpdateSelectionState();
        _timeline.MoveRequested += (_, args) => MoveTimelineItem(args.SourceIndex, args.InsertionIndex);
        _timelineViewport.Resize += (_, _) => UpdateTimelineSize();
        _orderComboBox.SelectedIndexChanged += (_, _) =>
        {
            ApplyRequestedOrder();
            PersistSortOrderPreference();
        };
        _incomingPathsTimer.Tick += (_, _) => FlushPendingPaths();
        _removeButton.Click += (_, _) => RemoveSelectedItem();
        _clearAllButton.Click += (_, _) => ClearAllItems();
        cancelButton.Click += (_, _) => Close();
        _joinButton.Click += (_, _) => ConfirmJoin();
        EnableDragDropOnControlTree(this);
        _timeline.DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
            {
                return;
            }

            var clientPoint = _timeline.PointToClient(new Point(e.X, e.Y));
            _pendingExternalDropIndex = _timeline.ShowExternalDropIndicator(clientPoint.X);
        };
        _timeline.DragLeave += (_, _) =>
        {
            _pendingExternalDropIndex = -1;
            _timeline.ClearExternalDropIndicator();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedItem();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.Left)
            {
                NudgeSelectedItem(-1);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.Right)
            {
                NudgeSelectedItem(1);
                e.Handled = true;
            }
        };
        Shown += async (_, _) =>
        {
            AddPaths(initialPaths);
            FlushPendingPaths();
            _incomingPathsTimer.Start();
            await EnsureItemsLoadedAsync().ConfigureAwait(true);
        };
        FormClosing += (_, _) =>
        {
            _disposed = true;
            _incomingPathsTimer.Stop();
            _loadCancellationSource.Cancel();
        };
        FormClosed += (_, _) => DisposeItems();
    }

    public JoinVideosSettings? Settings { get; private set; }

    internal IReadOnlyList<string> TimelinePaths => _items.Select(item => item.SourcePath).ToArray();

    internal Button AddVideosButton => _addButton;

    internal ComboBox OrderComboBox => _orderComboBox;

    internal bool IsClosingForTesting => _disposed;

    public void AddPathsThreadSafe(IReadOnlyList<string> paths)
    {
        var receivedPaths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (receivedPaths.Length == 0 || _disposed || IsDisposed)
        {
            return;
        }

        lock (_pendingPathsSync)
        {
            if (!_disposed)
            {
                _pendingPaths.AddRange(receivedPaths);
            }
        }
    }

    private void FlushPendingPaths()
    {
        string[] pending;
        lock (_pendingPathsSync)
        {
            pending = [.. _pendingPaths];
            _pendingPaths.Clear();
        }

        if (pending.Length == 0)
        {
            return;
        }

        AddReceivedPaths(pending);
    }

    private void AddReceivedPaths(IReadOnlyList<string> paths, int insertionIndex = -1)
    {
        if (_disposed || IsDisposed)
        {
            return;
        }

        AddPaths(paths, insertionIndex);
        _ = EnsureItemsLoadedAsync();
    }

    private Panel CreateTimelineToolbar(out Button addButton, out Button removeButton, out Button clearAllButton)
    {
        var toolbar = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 38,
            BackColor = FrameShiftTheme.Surface,
            Padding = new Padding(0, 0, 0, 4)
        };

        var orderLabel = new Label
        {
            AutoSize = true,
            Location = new Point(0, 9),
            Text = "Initial order:",
            ForeColor = FrameShiftTheme.TextPrimary
        };
        _orderComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _orderComboBox.FlatStyle = FlatStyle.Flat;
        _orderComboBox.BackColor = FrameShiftTheme.Surface;
        _orderComboBox.ForeColor = FrameShiftTheme.TextPrimary;
        _orderComboBox.Location = new Point(79, 5);
        _orderComboBox.Width = 190;
        _orderComboBox.Items.AddRange([
            "As received",
            "Natural file name",
            "Date created (oldest first)",
            "Date modified (oldest first)",
            "Custom"
        ]);
        _orderComboBox.SelectedIndex = ResolveSortOrderIndex(LoadUiSettings().JoinVideosSortOrder);

        var add = FrameShiftUiFactory.CreateActionButton("Add videos...", primary: false, 112);
        add.Height = 28;
        add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var remove = FrameShiftUiFactory.CreateActionButton("Remove", primary: false, 82);
        remove.Height = 28;
        remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        var clearAll = FrameShiftUiFactory.CreateActionButton("Clear all", primary: false, 90);
        clearAll.Height = 28;
        clearAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addButton = add;
        removeButton = remove;
        clearAllButton = clearAll;
        toolbar.Resize += (_, _) =>
        {
            remove.Location = new Point(toolbar.ClientSize.Width - remove.Width, 2);
            add.Location = new Point(remove.Left - FrameShiftUiMetrics.FooterButtonGap - add.Width, 2);
            clearAll.Location = new Point(add.Left - FrameShiftUiMetrics.FooterButtonGap - clearAll.Width, 2);
        };
        add.Click += (_, _) => AddVideosFromDialog();

        toolbar.Controls.Add(orderLabel);
        toolbar.Controls.Add(_orderComboBox);
        toolbar.Controls.Add(add);
        toolbar.Controls.Add(remove);
        toolbar.Controls.Add(clearAll);
        return toolbar;
    }

    private void AddPaths(IEnumerable<string> paths, int insertionIndex = -1)
    {
        var newItems = new List<JoinVideoTimelineItem>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var item = new JoinVideoTimelineItem(path, _nextReceivedIndex++);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!File.Exists(path))
            {
                item.IsLoaded = true;
                item.LoadError = "File not found.";
            }
            else if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                item.IsLoaded = true;
                item.LoadError = "Unsupported video format.";
            }

            newItems.Add(item);
        }

        if (_orderComboBox.SelectedIndex == 4 && insertionIndex >= 0 && insertionIndex <= _items.Count)
        {
            _items.InsertRange(insertionIndex, newItems);
        }
        else
        {
            _items.AddRange(newItems);
        }

        ApplyRequestedOrder();
        RefreshTimeline();
    }

    private async Task EnsureItemsLoadedAsync()
    {
        if (_loading || _disposed)
        {
            return;
        }

        _loading = true;
        try
        {
            while (!_loadCancellationSource.IsCancellationRequested)
            {
                var item = _items.FirstOrDefault(candidate => !candidate.IsLoaded && !candidate.IsLoading && !candidate.IsRemoved);
                if (item is null)
                {
                    break;
                }

                item.IsLoading = true;

                try
                {
                    var result = await _ffprobeRunner
                        .TryProbeJoinVideoAsync(_ffprobePath, item.SourcePath, _loadCancellationSource.Token)
                        .ConfigureAwait(true);
                    item.Probe = result.Probe;
                    item.LoadError = result.Probe is null
                        ? result.Error ?? "Could not inspect this video."
                        : null;

                    if (result.Probe is not null)
                    {
                        try
                        {
                            var previewSecond = Math.Min(Math.Max(result.Probe.DurationSeconds * 0.10d, 0d), 5d);
                            var thumbnail = await PreviewFrameHelper
                                .CaptureFrameAsync(_ffmpegPath, _ffmpegRunner, item.SourcePath, previewSecond, "Join Videos preview", _loadCancellationSource.Token)
                                .ConfigureAwait(true);
                            if (item.IsRemoved || _disposed)
                            {
                                thumbnail.Dispose();
                            }
                            else
                            {
                                item.Thumbnail = thumbnail;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // A frame preview is helpful but never blocks a valid clip from being joined.
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    item.LoadError = "Could not inspect this video.";
                }
                finally
                {
                    item.IsLoading = false;
                    item.IsLoaded = true;
                    RefreshTimeline();
                }
            }
        }
        finally
        {
            _loading = false;
            RefreshTimeline();
            if (!_disposed && _items.Any(item => !item.IsLoaded && !item.IsLoading && !item.IsRemoved))
            {
                _ = EnsureItemsLoadedAsync();
            }
        }
    }

    private void AddVideosFromDialog()
    {
        var selectedPaths = _videoPicker(this);
        if (selectedPaths is { Length: > 0 })
        {
            AddReceivedPaths(selectedPaths);
        }
    }

    private static string[]? ShowVideoPicker(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.m4v|All files|*.*",
            Multiselect = true,
            Title = "Add videos to Join Videos"
        };

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileNames : null;
    }

    private void ApplyRequestedOrder()
    {
        if (_items.Count < 2 || _orderComboBox.SelectedIndex is < 0 or 4)
        {
            return;
        }

        IEnumerable<JoinVideoTimelineItem> orderedQuery = _orderComboBox.SelectedIndex switch
        {
            0 => _items.OrderBy(item => item.ReceivedIndex),
            1 => _items.OrderBy(item => Path.GetFileName(item.SourcePath), NaturalFileNameComparer.Instance)
                .ThenBy(item => item.ReceivedIndex),
            2 => _items.OrderBy(item => item.CreatedUtc).ThenBy(item => Path.GetFileName(item.SourcePath), NaturalFileNameComparer.Instance),
            3 => _items.OrderBy(item => item.ModifiedUtc).ThenBy(item => Path.GetFileName(item.SourcePath), NaturalFileNameComparer.Instance),
            _ => _items
        };
        var ordered = orderedQuery.ToArray();

        _items.Clear();
        _items.AddRange(ordered);
        RefreshTimeline();
    }

    private void MoveTimelineItem(int sourceIndex, int insertionIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _items.Count)
        {
            return;
        }

        var destinationIndex = insertionIndex;
        if (destinationIndex > sourceIndex)
        {
            destinationIndex--;
        }

        destinationIndex = Math.Clamp(destinationIndex, 0, _items.Count - 1);
        if (destinationIndex == sourceIndex)
        {
            return;
        }

        var item = _items[sourceIndex];
        _items.RemoveAt(sourceIndex);
        _items.Insert(destinationIndex, item);
        _orderComboBox.SelectedIndex = 4;
        _timeline.SelectIndex(destinationIndex);
        RefreshTimeline();
    }

    private void EnableDragDropOnControlTree(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += HandleDragEnter;
        control.DragDrop += HandleDragDrop;
        foreach (Control child in control.Controls)
        {
            EnableDragDropOnControlTree(child);
        }
    }

    private void HandleDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] droppedPaths)
        {
            var insertionIndex = _pendingExternalDropIndex;
            _pendingExternalDropIndex = -1;
            _timeline.ClearExternalDropIndicator();
            AddReceivedPaths(droppedPaths, insertionIndex);
        }
    }

    private void RemoveSelectedItem()
    {
        var index = _timeline.SelectedIndex;
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        var item = _items[index];
        _items.RemoveAt(index);
        item.IsRemoved = true;
        item.DisposeThumbnail();
        _timeline.SelectIndex(Math.Min(index, _items.Count - 1));
        RefreshTimeline();
    }

    private void ClearAllItems()
    {
        if (_items.Count == 0)
        {
            return;
        }

        foreach (var item in _items)
        {
            item.IsRemoved = true;
            item.DisposeThumbnail();
        }

        _items.Clear();
        _timeline.SelectIndex(-1);
        RefreshTimeline();
    }

    private void NudgeSelectedItem(int direction)
    {
        var index = _timeline.SelectedIndex;
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= _items.Count)
        {
            return;
        }

        (_items[index], _items[targetIndex]) = (_items[targetIndex], _items[index]);
        _orderComboBox.SelectedIndex = 4;
        _timeline.SelectIndex(targetIndex);
        RefreshTimeline();
    }

    private void PersistSortOrderPreference()
    {
        var key = GetSortOrderKey(_orderComboBox.SelectedIndex);
        if (key is null)
        {
            return;
        }

        var settings = LoadUiSettings();
        settings.JoinVideosSortOrder = key;
        SaveUiSettings(settings);
    }

    private FrameShiftUiSettings LoadUiSettings()
    {
        return _uiSettingsPathForTesting is null
            ? FrameShiftUiSettings.Load()
            : FrameShiftUiSettings.Load(_uiSettingsPathForTesting);
    }

    private void SaveUiSettings(FrameShiftUiSettings settings)
    {
        if (_uiSettingsPathForTesting is null)
        {
            settings.Save();
        }
        else
        {
            settings.Save(_uiSettingsPathForTesting);
        }
    }

    internal static int ResolveSortOrderIndex(string? savedKey)
    {
        var index = Array.IndexOf(SortOrderKeys, savedKey);
        return index >= 0 ? index : DefaultSortOrderIndex;
    }

    internal static string? GetSortOrderKey(int comboIndex)
    {
        return comboIndex >= 0 && comboIndex < SortOrderKeys.Length ? SortOrderKeys[comboIndex] : null;
    }

    private void RefreshTimeline()
    {
        if (_disposed)
        {
            return;
        }

        _timeline.SetItems(_items);
        UpdateTimelineSize();
        UpdateSelectionState();
        UpdateStatus();
    }

    private void UpdateTimelineSize()
    {
        if (_timelineViewport.IsDisposed || _timeline.IsDisposed)
        {
            return;
        }

        _timeline.Width = Math.Max(1, _timelineViewport.ClientSize.Width);
        _timeline.Height = 182;
    }

    private void UpdateSelectionState()
    {
        _removeButton.Enabled = _timeline.SelectedIndex >= 0 && _timeline.SelectedIndex < _items.Count;
    }

    private void UpdateStatus()
    {
        var invalid = _items.FirstOrDefault(item => item.IsLoaded &&
            (item.Probe is null || !item.Probe.HasVideo || item.Probe.DurationSeconds <= 0d || !string.IsNullOrWhiteSpace(item.LoadError)));
        var pending = _items.Any(item => !item.IsLoaded);
        var totalDuration = _items.Sum(item => item.DurationSeconds);

        if (invalid is not null)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = $"Remove or replace invalid clip: {Path.GetFileName(invalid.SourcePath)}";
            _joinButton.Enabled = false;
            return;
        }

        if (_items.Count < 2)
        {
            _statusLabel.ForeColor = FrameShiftTheme.TextSecondary;
            _statusLabel.Text = "Add at least two videos. The same file may be added more than once.";
            _joinButton.Enabled = false;
            return;
        }

        if (pending)
        {
            _statusLabel.ForeColor = FrameShiftTheme.TextSecondary;
            _statusLabel.Text = "Inspecting clips and generating previews...";
            _joinButton.Enabled = false;
            return;
        }

        var duration = TimeSpan.FromSeconds(totalDuration);
        _statusLabel.ForeColor = FrameShiftTheme.TextSecondary;
        _statusLabel.Text = $"{_items.Count} clips · {duration:h\\:mm\\:ss} total · Automatic mode uses direct concat only when stream signatures match; otherwise SDR H.264/AAC MP4.";
        _joinButton.Enabled = true;
    }

    private void ConfirmJoin()
    {
        if (!_joinButton.Enabled)
        {
            return;
        }

        Settings = new JoinVideosSettings
        {
            InputPaths = _items.Select(item => item.SourcePath).ToList(),
            Mode = JoinVideosMode.Auto
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private void DisposeItems()
    {
        _disposed = true;
        _incomingPathsTimer.Dispose();
        _loadCancellationSource.Cancel();
        foreach (var item in _items)
        {
            item.DisposeThumbnail();
        }
    }
}
