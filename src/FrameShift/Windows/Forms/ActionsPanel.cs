using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Windows.Helpers;

namespace FrameShift.Windows.Forms;

/// <summary>
/// Right-hand actions panel of the main window. Given the queue and the current
/// selection it shows only the applicable actions, grouped by category, with a
/// "· N" badge (how many scoped files each would process) and the single-file rule
/// applied (D2). A search box and type chips narrow what is shown. Clicking an
/// enabled action raises <see cref="ActionInvoked"/> with the files it would run on;
/// the host wires that to <see cref="ActionLauncher"/>.
/// </summary>
public sealed class ActionsPanel : UserControl
{
    private static readonly Font s_headerFont = new("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_chipFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_buttonFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly MediaFamily[] FamilyOrder = { MediaFamily.Video, MediaFamily.Audio, MediaFamily.Image, MediaFamily.Other };

    private readonly TextBox _searchBox;
    private readonly FlowLayoutPanel _chipsPanel;
    private readonly FlowLayoutPanel _content;
    private readonly ToolTip _toolTip = new();

    private IReadOnlyList<string> _allFiles = Array.Empty<string>();
    private IReadOnlyList<string> _selectedFiles = Array.Empty<string>();
    private IReadOnlyList<string> _currentScope = Array.Empty<string>();
    private MediaFamily? _familyFilter;
    private string _search = string.Empty;

    public ActionsPanel()
    {
        BackColor = FrameShiftTheme.Surface;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = FrameShiftTheme.Surface,
            Padding = new Padding(12, 10, 12, 10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Search actions",
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 8)
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _search = _searchBox.Text.Trim();
            Rebuild();
        };

        _chipsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = FrameShiftTheme.Surface,
            Margin = new Padding(0, 0, 0, 6),
            Padding = Padding.Empty
        };

        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            BackColor = FrameShiftTheme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        root.Controls.Add(_searchBox, 0, 0);
        root.Controls.Add(_chipsPanel, 0, 1);
        root.Controls.Add(_content, 0, 2);
        Controls.Add(root);
    }

    public event EventHandler<ActionInvokedEventArgs>? ActionInvoked;

    /// <summary>Updates the panel from the current queue and selection, then rebuilds.</summary>
    public void SetFiles(IReadOnlyList<string> allFiles, IReadOnlyList<string> selectedFiles)
    {
        _allFiles = allFiles?.ToArray() ?? Array.Empty<string>();
        _selectedFiles = selectedFiles?.ToArray() ?? Array.Empty<string>();
        Rebuild();
    }

    private void Rebuild()
    {
        var baseScope = _selectedFiles.Count > 0 ? _selectedFiles : _allFiles;
        var baseCounts = ActionScopeResolver.FamilyCounts(baseScope);

        // Drop a stale family filter that no longer matches anything in the base scope.
        if (_familyFilter is { } filter && baseCounts.GetValueOrDefault(filter) == 0)
        {
            _familyFilter = null;
        }

        _currentScope = ActionScopeResolver.ResolveScope(_allFiles, _selectedFiles, _familyFilter);
        var scopeCounts = ActionScopeResolver.FamilyCounts(_currentScope);

        SuspendLayout();
        _content.SuspendLayout();
        try
        {
            BuildChips(baseCounts);
            BuildActions(scopeCounts);
        }
        finally
        {
            _content.ResumeLayout();
            ResumeLayout();
        }
    }

    private void BuildChips(IReadOnlyDictionary<MediaFamily, int> baseCounts)
    {
        _chipsPanel.Controls.Clear();
        if (_currentScope.Count == 0 && _allFiles.Count == 0)
        {
            return;
        }

        _chipsPanel.Controls.Add(CreateChip("All", null, _familyFilter is null));

        foreach (var family in FamilyOrder)
        {
            if (baseCounts.TryGetValue(family, out var count) && count > 0)
            {
                _chipsPanel.Controls.Add(CreateChip(
                    $"{FamilyLabel(family)} {count}",
                    family,
                    _familyFilter == family));
            }
        }
    }

    private void BuildActions(IReadOnlyDictionary<MediaFamily, int> scopeCounts)
    {
        _content.Controls.Clear();

        var applicable = ActionScopeResolver.Resolve(_currentScope);
        if (!string.IsNullOrEmpty(_search))
        {
            applicable = applicable
                .Where(a => a.Entry.DisplayName.Contains(_search, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        Control? last = null;
        ActionCategory? currentCategory = null;

        foreach (var availability in applicable)
        {
            if (availability.Entry.Category != currentCategory)
            {
                if (last is not null)
                {
                    _content.SetFlowBreak(last, true);
                }

                currentCategory = availability.Entry.Category;
                var header = CreateGroupHeader(currentCategory.Value, scopeCounts);
                _content.Controls.Add(header);
                _content.SetFlowBreak(header, true);
                last = header;
            }

            var button = CreateActionButton(availability);
            _content.Controls.Add(button);
            last = button;
        }
    }

    private Label CreateGroupHeader(ActionCategory category, IReadOnlyDictionary<MediaFamily, int> scopeCounts)
    {
        var count = category switch
        {
            ActionCategory.Video => scopeCounts.GetValueOrDefault(MediaFamily.Video),
            ActionCategory.Audio => scopeCounts.GetValueOrDefault(MediaFamily.Audio),
            ActionCategory.Image => scopeCounts.GetValueOrDefault(MediaFamily.Image),
            _ => _currentScope.Count
        };

        return new Label
        {
            AutoSize = true,
            Text = $"{CategoryLabel(category)}  ·  {count}",
            Font = s_headerFont,
            ForeColor = FrameShiftTheme.TextSecondary,
            Margin = new Padding(0, 10, 0, 4)
        };
    }

    private Button CreateActionButton(ActionAvailability availability)
    {
        var text = availability.Entry.DisplayName;
        if (availability.Entry.Arity != ActionArity.Single)
        {
            text += $"   ·  {availability.MatchingFileCount}";
        }

        var button = new Button
        {
            Text = text,
            Width = 152,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 4, 0),
            Margin = new Padding(0, 0, 8, 8),
            Font = s_buttonFont,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.TextPrimary,
            Cursor = Cursors.Hand,
            Enabled = availability.IsEnabled,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        button.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;

        if (!availability.IsEnabled && !string.IsNullOrWhiteSpace(availability.DisabledReason))
        {
            _toolTip.SetToolTip(button, availability.DisabledReason);
        }

        var entry = availability.Entry;
        button.Click += (_, _) =>
        {
            var files = ActionScopeResolver.MatchingFiles(entry, _currentScope);
            if (files.Count > 0)
            {
                ActionInvoked?.Invoke(this, new ActionInvokedEventArgs(entry, files));
            }
        };

        return button;
    }

    private Label CreateChip(string text, MediaFamily? family, bool active)
    {
        var chip = new Label
        {
            Text = text,
            AutoSize = true,
            Font = s_chipFont,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = active ? FrameShiftTheme.AccentSoft : FrameShiftTheme.PageBackground,
            ForeColor = active ? FrameShiftTheme.SecondaryBlue : FrameShiftTheme.TextSecondary
        };

        chip.Click += (_, _) =>
        {
            _familyFilter = family;
            Rebuild();
        };

        return chip;
    }

    private static string CategoryLabel(ActionCategory category) => category switch
    {
        ActionCategory.Video => "Video",
        ActionCategory.Audio => "Audio",
        ActionCategory.Image => "Image",
        _ => "General"
    };

    private static string FamilyLabel(MediaFamily family) => family switch
    {
        MediaFamily.Video => "Video",
        MediaFamily.Audio => "Audio",
        MediaFamily.Image => "Image",
        _ => "Other"
    };
}

/// <summary>Carries the invoked action and the scoped files it should run on.</summary>
public sealed class ActionInvokedEventArgs : EventArgs
{
    public ActionInvokedEventArgs(ActionCatalogEntry entry, IReadOnlyList<string> files)
    {
        Entry = entry;
        Files = files;
    }

    public ActionCatalogEntry Entry { get; }

    public IReadOnlyList<string> Files { get; }
}
