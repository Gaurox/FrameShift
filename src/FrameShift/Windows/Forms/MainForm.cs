using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
/// (no 15-file Explorer cap). Shows a centered drop zone while the queue is empty.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Font s_titleFont = new("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_emptyTitleFont = new("Segoe UI Semibold", 15F, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly Font s_bodyFont = new("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

    private readonly FileQueuePanel _queuePanel;
    private readonly ActionsPanel _actionsPanel;
    private readonly SplitContainer _split;
    private readonly Panel _emptyState;

    public MainForm()
        : this(Array.Empty<string>())
    {
    }

    public MainForm(IEnumerable<string> startupPaths)
    {
        FrameShiftWindowChrome.Apply(this, "FrameShift");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 540);
        Size = new Size(920, 620);
        BackColor = FrameShiftTheme.PageBackground;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        _queuePanel = new FileQueuePanel { Dock = DockStyle.Fill };
        _queuePanel.QueueChanged += (_, _) => OnQueueChanged();
        _queuePanel.SelectionChanged += (_, _) => RefreshActions();

        _actionsPanel = new ActionsPanel { Dock = DockStyle.Fill };
        _actionsPanel.ActionInvoked += OnActionInvoked;

        // Note: Panel1MinSize/Panel2MinSize are deliberately left at their small defaults.
        // Setting large min sizes before the control is realized throws during layout
        // ("SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize").
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = FrameShiftTheme.SurfaceBorder,
            SplitterWidth = 1,
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
            Padding = new Padding(16, 8, 16, 8)
        };
        body.Controls.Add(_split);
        body.Controls.Add(_emptyState);

        Controls.Add(BuildTitleBar());
        Controls.Add(BuildFooter());
        Controls.Add(body);
        // Fill must be behind the docked title/footer; re-add body last keeps it filling.
        body.BringToFront();
        _split.BringToFront();

        Load += (_, _) => UpdateState();

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

    private void SetSplitterDistance()
    {
        if (_split.Width <= 0)
        {
            return;
        }

        const int leftMin = 200;
        const int rightMin = 280;
        var target = (int)(_split.Width * 0.4);
        var max = _split.Width - rightMin - _split.SplitterWidth;
        if (max < leftMin)
        {
            return;
        }

        _split.SplitterDistance = Math.Clamp(target, leftMin, max);
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

    private Panel BuildTitleBar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = FrameShiftTheme.Surface,
            Padding = new Padding(16, 0, 12, 0)
        };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(16, 12),
            Text = "FrameShift",
            Font = s_titleFont,
            ForeColor = FrameShiftTheme.TextPrimary
        };

        var version = new Label
        {
            AutoSize = true,
            Location = new Point(title.Right + 120, 18),
            Text = $"v{Application.ProductVersion}",
            Font = s_bodyFont,
            ForeColor = FrameShiftTheme.TextMuted
        };

        var settings = new Button
        {
            Text = "Settings",
            Dock = DockStyle.Right,
            Width = 96,
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.Surface,
            ForeColor = FrameShiftTheme.SecondaryBlue,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        settings.FlatAppearance.BorderColor = FrameShiftTheme.PrimaryBlue;
        settings.FlatAppearance.MouseOverBackColor = FrameShiftTheme.AccentSoft;
        settings.Click += (_, _) =>
        {
            using var dialog = new SettingsForm();
            dialog.ShowDialog(this);
        };

        var separator = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = FrameShiftTheme.SurfaceBorder
        };

        panel.Controls.Add(title);
        panel.Controls.Add(version);
        panel.Controls.Add(settings);
        panel.Controls.Add(separator);
        return panel;
    }

    private static Panel BuildFooter()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = FrameShiftTheme.PageBackground,
            Padding = new Padding(18, 0, 18, 0)
        };

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Outputs are saved next to each source file · nothing is uploaded",
            Font = s_bodyFont,
            ForeColor = FrameShiftTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft
        });

        return panel;
    }

    private Panel BuildEmptyState()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = FrameShiftTheme.PageBackground
        };
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        var box = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.None,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = FrameShiftTheme.Surface,
            Padding = new Padding(48, 34, 48, 34)
        };
        box.Paint += (_, e) =>
        {
            var rect = new Rectangle(0, 0, box.Width - 1, box.Height - 1);
            using var pen = new Pen(FrameShiftTheme.PrimaryBlue) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, rect);
        };

        var title = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = "Drop files here",
            Font = s_emptyTitleFont,
            ForeColor = FrameShiftTheme.TextPrimary,
            Margin = new Padding(0, 0, 0, 6)
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = "or use Browse — videos, audio, images",
            Font = s_bodyFont,
            ForeColor = FrameShiftTheme.TextMuted,
            Margin = new Padding(0, 0, 0, 14)
        };

        var browse = new Button
        {
            Text = "Browse…",
            Anchor = AnchorStyles.None,
            Width = FrameShiftUiMetrics.PrimaryButtonWidth,
            Height = FrameShiftUiMetrics.FooterButtonHeight,
            FlatStyle = FlatStyle.Flat,
            BackColor = FrameShiftTheme.SecondaryBlue,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        browse.FlatAppearance.BorderColor = FrameShiftTheme.SecondaryBlue;
        browse.FlatAppearance.MouseOverBackColor = FrameShiftTheme.PrimaryBlue;
        browse.Click += (_, _) => _queuePanel.PromptForFiles();

        box.Controls.Add(title);
        box.Controls.Add(subtitle);
        box.Controls.Add(browse);

        host.Controls.Add(box, 0, 1);
        return host;
    }
}
