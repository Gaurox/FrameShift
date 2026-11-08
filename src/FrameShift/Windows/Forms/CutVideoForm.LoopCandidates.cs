using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

public sealed partial class CutVideoForm
{
    private bool _updatingLoopResults;

    private DataGridView CreateLoopCandidateGrid()
    {
        var grid = new LoopCandidateGrid
        {
            Name = "loopResults", AccessibleName = L.Get("Loop_Candidates"),
            AccessibleDescription = L.Get("Loop_CandidateKeyboard"),
            ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            BackgroundColor = FrameShiftTheme.Surface, BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.None, GridColor = FrameShiftTheme.SurfaceBorder,
            EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        grid.DefaultCellStyle.BackColor = FrameShiftTheme.Surface;
        grid.DefaultCellStyle.ForeColor = FrameShiftTheme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = FrameShiftTheme.AccentSoft;
        grid.DefaultCellStyle.SelectionForeColor = FrameShiftTheme.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.BackColor = FrameShiftTheme.Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = FrameShiftTheme.TextPrimary;
        foreach (var (name, title) in new[] { ("Start", L.Get("CutVideo_Start")), ("End", L.Get("Loop_ColumnEnd")), ("Duration", L.Get("Loop_ColumnDuration")), ("Junction", L.Get("Loop_ColumnJunction")) })
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = title,
                SortMode = name == "Duration" ? DataGridViewColumnSortMode.Automatic : DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = name == "Junction" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
            });
        grid.Columns["Duration"]!.DefaultCellStyle.Format = L.Get("Loop_DurationFormat");
        FrameShiftGridUi.Apply(grid, ("Start", 125), ("End", 125), ("Duration", 100));
        void Metrics()
        {
            grid.Height = FrameShiftUiMetrics.ToPixels(grid, 180);
            grid.Columns["Junction"]!.MinimumWidth = FrameShiftUiMetrics.ToPixels(grid, 120);
        }
        grid.HandleCreated += (_, _) => Metrics();
        grid.DpiChangedAfterParent += (_, _) => Metrics();
        Metrics();
        grid.SelectionChanged += (_, _) => { if (grid.SelectedRows.Count > 0) SelectLoopCandidate(); };
        grid.CellClick += (_, e) => { if (e.RowIndex >= 0) SelectLoopCandidate(); };
        grid.ActivateCandidate = SelectLoopCandidate;
        return grid;
    }

    internal void ApplyLoopAnalysis(VideoLoopAnalysis analysis)
    {
        if (_closing || IsDisposed) return;
        var current = _loopCandidate;
        var hadRows = _loopResults.Rows.Count > 0;
        var firstVisible = _loopResults.FirstDisplayedScrollingRowIndex >= 0
            ? _loopResults.Rows[_loopResults.FirstDisplayedScrollingRowIndex].Tag as VideoLoopCandidate : null;
        var sortedColumn = _loopResults.SortedColumn;
        var descending = _loopResults.SortOrder == SortOrder.Descending;
        _updatingLoopResults = true;
        try
        {
            _loopResults.Rows.Clear();
            foreach (var candidate in analysis.Candidates)
            {
                var index = _loopResults.Rows.Add(CutVideoMath.FormatPreciseTime(candidate.StartSeconds),
                    CutVideoMath.FormatPreciseTime(candidate.EndSeconds), candidate.DurationSeconds, candidate.JunctionAssessment);
                _loopResults.Rows[index].Tag = candidate;
            }
            if (sortedColumn is not null) _loopResults.Sort(sortedColumn, descending
                ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending);
            _loopResults.ClearSelection();
            var match = _loopResults.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
                row.Tag is VideoLoopCandidate c && current is not null &&
                c.StartSeconds == current.StartSeconds && c.EndSeconds == current.EndSeconds);
            if (match is not null) _loopResults.CurrentCell = match.Cells[0];
            else if (current is null && !_loopCustomSelection && _loopResults.Rows.Count > 0)
                _loopResults.CurrentCell = _loopResults.Rows[0].Cells[0];
            else _loopResults.CurrentCell = null;
            if (_loopResults.CurrentRow is { } selected) selected.Selected = true;
            if (firstVisible is not null && _loopResults.IsHandleCreated)
            {
                var visible = _loopResults.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => row.Tag is VideoLoopCandidate c &&
                    c.StartSeconds == firstVisible.StartSeconds && c.EndSeconds == firstVisible.EndSeconds);
                if (visible is not null) _loopResults.FirstDisplayedScrollingRowIndex = visible.Index;
            }
        }
        finally { _updatingLoopResults = false; }
        if (_loopResults.SelectedRows.Count > 0)
        {
            if (current is null) SelectLoopCandidate();
            else _loopCandidate = (VideoLoopCandidate)_loopResults.SelectedRows[0].Tag!;
        }
        _loopStatus.Text = analysis.Candidates.Count == 0
            ? L.Get("Loop_NoCandidates")
            : L.Format("Loop_CandidatesFound", analysis.Candidates.Count);
        if (!analysis.SearchCompleted) _loopStatus.Text = L.Format("Loop_SearchInProgress", analysis.Candidates.Count);
        // A new search need not rediscover the chosen seam. Keep its native timestamps,
        // rather than silently rounding the user's selection through average-FPS fields.
        if (current is not null && _loopResults.SelectedRows.Count == 0)
            _loopStatus.Text += L.Get("Loop_PreviousSelection");
        if (analysis.Limits != VideoLoopSearchLimits.None)
            _loopStatus.Text += L.Get("Loop_LimitedCoverage");
        if (analysis.Limits.HasFlag(VideoLoopSearchLimits.TemporalCoverage))
            _loopStatus.Text += L.Format("Loop_TemporalCoverage", analysis.Diagnostics.CoveredSeconds);
        UpdateLoopCommands();
        if (!hadRows) RevealLoopControl(_previewLoop);
    }

    private void SelectLoopCandidate()
    {
        var row = _loopResults.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault() ?? _loopResults.CurrentRow;
        if (_updatingLoopResults || _closing || row?.Tag is not VideoLoopCandidate c) return;
        _updatingLoopResults = true;
        try { row.Selected = true; }
        finally { _updatingLoopResults = false; }
        InvalidateLoopPreview();
        // Rows are immutable snapshots. Editing the timeline never writes back into them.
        _loopContext = LoopContext.Natural;
        _selection.StartFrame = Math.Clamp((int)Math.Round(c.StartSeconds * _fps) + 1, 1, _totalFrames);
        _selection.EndFrame = Math.Clamp((int)Math.Round(c.EndSeconds * _fps), _selection.StartFrame, _totalFrames);
        _candidateFrames = (_selection.StartFrame, _selection.EndFrame);
        _loopCandidate = c;
        SetNativeCandidate(c);
        _loopCustomSelection = false;
        RefreshSelectionUi(true);
        UpdateLoopCommands();
    }

    private void MarkCustomLoopSelection()
    {
        _loopCandidate = null;
        _loopCustomSelection = true;
        _updatingLoopResults = true;
        try { _loopResults.ClearSelection(); }
        finally { _updatingLoopResults = false; }
        InvalidateLoopPreview();
    }

    private sealed class LoopCandidateGrid : DataGridView
    {
        public Action? ActivateCandidate { get; set; }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData is Keys.Enter or Keys.Space) { ActivateCandidate?.Invoke(); return true; }
            return base.ProcessDialogKey(keyData);
        }
        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            if (e.KeyData is Keys.Enter or Keys.Space) { ActivateCandidate?.Invoke(); return true; }
            return base.ProcessDataGridViewKey(e);
        }
    }
}
