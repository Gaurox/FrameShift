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
/// Left-hand file queue of the main window: an ordered, de-duplicated list of files
/// with type, name and a remove affordance. Backed by <see cref="FileQueueModel"/>.
/// Accepts files via drag-and-drop, the "Add…" button, or <see cref="AddFiles"/>.
/// Raises <see cref="QueueChanged"/> when the set changes and <see cref="SelectionChanged"/>
/// when the selection changes, so the actions panel can rescope.
/// </summary>
public sealed class FileQueuePanel : UserControl
{
    private const string MediaFileFilter =
        "Media and PDF files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.m4v;*.mp3;*.wav;*.wave;*.flac;*.m4a;*.ogg;*.aac;*.wma;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.pdf|All files|*.*";


    private readonly FileQueueModel _model = new();
    private readonly DataGridView _grid;
    private readonly Label _countLabel;

    public FileQueuePanel()
    {
        AutoScaleMode = AutoScaleMode.Inherit;
        BackColor = FrameShiftTheme.Surface;
        ForeColor = FrameShiftTheme.TextPrimary;
        AllowDrop = true;

        _grid = CreateGrid();
        _grid.CellContentClick += OnCellContentClick;
        _grid.RowPrePaint += OnRowPrePaint;
        _grid.SelectionChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _grid.KeyDown += OnGridKeyDown;
        _grid.AllowDrop = true;
        _grid.DragEnter += OnDragEnter;
        _grid.DragDrop += OnDragDrop;

        var header = BuildHeader(out _countLabel);
        var hint = FrameShiftUiFactory.CreateWrappingLabel("Drop files here to add · Delete removes selected files");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(hint, 0, 2);
        Controls.Add(root);
        void Metrics()
        {
            root.Padding = FrameShiftUiMetrics.ToPixels(FrameShiftUiMetrics.StandardSectionPadding, DeviceDpi);
            var gap = FrameShiftUiMetrics.ToPixels(this, FrameShiftUiMetrics.BlockGap);
            header.Margin = new Padding(0, 0, 0, gap);
            _grid.Margin = Padding.Empty;
            hint.Margin = new Padding(0, gap, 0, 0);
        }
        HandleCreated += (_, _) => Metrics();
        DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        UpdateHeader();
    }

    /// <summary>Queued file paths in insertion order.</summary>
    public IReadOnlyList<string> Items => _model.Items;

    /// <summary>Selected file paths in display order (empty when nothing is selected).</summary>
    public IReadOnlyList<string> SelectedPaths
    {
        get
        {
            var paths = new List<string>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Selected && row.Tag is string path)
                {
                    paths.Add(path);
                }
            }

            return paths;
        }
    }

    public event EventHandler? QueueChanged;
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Adds files to the queue. Non-existent paths and directories are ignored;
    /// paths are normalized and de-duplicated. Returns how many were newly added.
    /// </summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        if (paths is null)
        {
            return 0;
        }

        // DataGridView selects the first added row by default. Keep an existing user
        // selection, but leave a programmatically populated queue unscoped.
        var preserveSelection = _grid.SelectedRows.Count > 0;
        var added = 0;
        _grid.SuspendLayout();
        try
        {
            foreach (var raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(raw);
                }
                catch
                {
                    continue;
                }

                if (!File.Exists(fullPath) || !_model.Add(fullPath))
                {
                    continue;
                }

                AddGridRow(fullPath);
                added++;
            }
        }
        finally
        {
            _grid.ResumeLayout();
        }

        if (added > 0)
        {
            if (!preserveSelection)
            {
                _grid.ClearSelection();
                _grid.CurrentCell = null;
            }

            UpdateHeader();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        return added;
    }

    /// <summary>Opens the file picker and adds the chosen files to the queue.</summary>
    public void PromptForFiles() => BrowseForFiles();

    public void Clear()
    {
        if (_model.Count == 0)
        {
            return;
        }

        _model.Clear();
        _grid.Rows.Clear();
        UpdateHeader();
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddGridRow(string fullPath)
    {
        var kind = MediaFileClassifier.Classify(fullPath);
        var rowIndex = _grid.Rows.Add(KindLabel(kind), Path.GetFileName(fullPath), "×");
        _grid.Rows[rowIndex].Tag = fullPath;
        _grid.Rows[rowIndex].Cells["FileName"].ToolTipText = fullPath;
        _grid.Rows[rowIndex].Cells["Remove"].ToolTipText = $"Remove {Path.GetFileName(fullPath)}";
    }

    private void OnCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_grid.Columns[e.ColumnIndex].Name != "Remove")
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].Tag is string path && RemovePathCore(path))
        {
            UpdateHeader();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete)
        {
            return;
        }

        var selected = SelectedPaths;
        if (selected.Count == 0)
        {
            return;
        }

        var removedAny = false;
        foreach (var path in selected)
        {
            removedAny |= RemovePathCore(path);
        }

        if (removedAny)
        {
            UpdateHeader();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    // Removes from the model and the grid without raising events (callers batch the event).
    private bool RemovePathCore(string fullPath)
    {
        if (!_model.Remove(fullPath))
        {
            return false;
        }

        for (var index = _grid.Rows.Count - 1; index >= 0; index--)
        {
            if (_grid.Rows[index].Tag is string rowPath &&
                string.Equals(rowPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                _grid.Rows.RemoveAt(index);
                break;
            }
        }

        return true;
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
            AddFiles(dropped);
        }
    }

    private void UpdateHeader()
    {
        _countLabel.Text = BuildCountText(_model.Count, _model.CountByKind());
    }

    private static string BuildCountText(int total, IReadOnlyDictionary<MediaFamily, int> byKind)
    {
        if (total == 0)
        {
            return "No files yet";
        }

        var head = total == 1 ? "1 file" : $"{total} files";

        var parts = new List<string>();
        foreach (var kind in new[] { MediaFamily.Video, MediaFamily.Audio, MediaFamily.Image, MediaFamily.Document, MediaFamily.Other })
        {
            if (byKind.TryGetValue(kind, out var count) && count > 0)
            {
                parts.Add($"{count} {KindLabel(kind)}");
            }
        }

        return parts.Count > 0 ? $"{head}  ·  {string.Join(", ", parts)}" : head;
    }

    private static string KindLabel(MediaFamily kind) => kind switch
    {
        MediaFamily.Video => "video",
        MediaFamily.Audio => "audio",
        MediaFamily.Image => "image",
        MediaFamily.Document => "PDF",
        _ => "other"
    };

    private Panel BuildHeader(out Label countLabel)
    {
        countLabel = FrameShiftUiFactory.CreateWrappingLabel("No files yet");
        var add = FrameShiftUiFactory.CreateMeasuredActionButton("Add…", false);
        var clear = FrameShiftUiFactory.CreateMeasuredActionButton("Clear", false);
        add.Click += (_, _) => BrowseForFiles();
        clear.Click += (_, _) => Clear();
        return FrameShiftUiFactory.CreateVerticalStack(countLabel, FrameShiftUiFactory.CreateChoiceRow(add, clear));
    }
    private void BrowseForFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add files",
            Multiselect = true,
            Filter = MediaFileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddFiles(dialog.FileNames);
        }
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AccessibleName = "Files queue",
            AccessibleDescription = "Select files with the arrow keys. Delete removes selected files from the queue.",
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            MultiSelect = true,
            ReadOnly = true,
            RowHeadersVisible = false,
            ColumnHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = FrameShiftTheme.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            GridColor = FrameShiftTheme.Surface,
            EnableHeadersVisualStyles = false,
            ScrollBars = ScrollBars.Vertical
        };

        grid.DefaultCellStyle.BackColor = FrameShiftTheme.Surface;
        grid.DefaultCellStyle.ForeColor = FrameShiftTheme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = FrameShiftTheme.AccentSoft;
        grid.DefaultCellStyle.SelectionForeColor = FrameShiftTheme.TextPrimary;
        grid.DefaultCellStyle.Padding = new Padding(0, 6, 0, 6);
        grid.RowTemplate.Height = 36;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Kind",
            Width = 64,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { ForeColor = FrameShiftTheme.TextMuted }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FileName",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 100
        });
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Remove",
            HeaderText = "Remove file",
            CellTemplate = new FrameShift.Windows.Controls.FrameShiftGridButtonCell(),
            Width = 40,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Text = "×",
            UseColumnTextForButtonValue = true,
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = FrameShiftTheme.Surface,
                ForeColor = FrameShiftTheme.AccentText,
                SelectionBackColor = FrameShiftTheme.AccentSoft,
                SelectionForeColor = FrameShiftTheme.AccentText
            }
        });

        FrameShiftGridUi.Apply(grid, ("Kind", 64), ("Remove", 40));
        return grid;
    }

    private void OnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        var rowBounds = _grid.GetRowDisplayRectangle(e.RowIndex, false);
        using var pen = new Pen(FrameShiftTheme.SurfaceBorder);
        e.Graphics.DrawLine(
            pen,
            rowBounds.Left,
            rowBounds.Bottom - 1,
            rowBounds.Left + _grid.ClientSize.Width,
            rowBounds.Bottom - 1);
    }
}
