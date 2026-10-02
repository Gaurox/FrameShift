using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Controls;
using FrameShift.Windows.Forms;
using FrameShift.Windows.Helpers;
using FrameShift.Windows.ProgressUI;
using Xunit;

namespace FrameShift.Tests;

// Dispatch to hidden controls only. No desktop input, modal window or clipboard writes.
public sealed class UiKeyboardTests
{
    [Theory]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Space)]
    public void Header_ReadsCompleteMetadata_WithoutActivatingTheHostDefaultButton(Keys key)
    {
        StaTest.Run(() =>
        {
            var metadata = @"Source: E:\Vidéos avec espaces et accents\" + new string('é', 400) + ".mp4";
            using var host = new Form();
            var header = NewHeader(metadata);
            host.Controls.Add(header);
            var start = FrameShiftUiFactory.CreateMeasuredActionButton("Start", true);
            host.Controls.Add(start);
            host.AcceptButton = start;
            var starts = 0;
            start.Click += (_, _) => starts++;
            var openings = 0;
            header.DisplayDetails = (dialog, owner) =>
            {
                openings++;
                Assert.Same(host, owner);
                var details = Descendants(dialog).OfType<TextBox>().Single();
                Assert.Equal(metadata, details.Text);
                Assert.True(details.ReadOnly && details.Multiline);
                Assert.Equal(ScrollBars.Vertical, details.ScrollBars);
                Assert.Same(dialog.AcceptButton, dialog.CancelButton);
                Assert.False(dialog.Visible);
            };
            var size = header.GetPreferredSize(new Size(400, 0));
            Assert.False(InputKey(header, key));
            Assert.True(DialogKey(header, key));
            Assert.Equal(1, openings);
            Assert.Equal(0, starts);
            Assert.Equal(size, header.GetPreferredSize(new Size(400, 0)));
            Assert.Contains(metadata, header.AccessibleDescription);
            Assert.False(host.Visible);
        });
    }

    [Fact]
    public void Header_CopyShortcutAndMenu_UseUnabridgedText_AndEmptyHeadersAreSkipped()
    {
        StaTest.Run(() =>
        {
            using var header = NewHeader("Métadonnées complètes " + new string('x', 600));
            var copies = new List<string>();
            header.CopyDetailsText = copies.Add;
            Assert.True(CommandKey(header, Keys.Control | Keys.C));
            ((ToolStripMenuItem)header.ContextMenuStrip!.Items[1]).PerformClick();
            Assert.Equal(new[] { header.SubtitleLabel.Text, header.SubtitleLabel.Text }, copies);
            header.SubtitleLabel.Text = "";
            Assert.False(header.TabStop);
            Assert.All(header.ContextMenuStrip.Items.Cast<ToolStripItem>(), item => Assert.False(item.Enabled));
            CommandKey(header, Keys.Control | Keys.C);
            Assert.Equal(2, copies.Count);
            header.SubtitleLabel.Text = "New source";
            Assert.True(header.TabStop);
            Assert.Contains("New source", header.AccessibleDescription);
        });
    }

    [Fact]
    public void Timeline_NavigatesAllClipsIncludingTinyOnes_ClampsAtBothEnds()
    {
        StaTest.Run(() =>
        {
            using var timeline = new JoinVideosTimelineControl { Size = new Size(40, 182) };
            timeline.SetItems(Enumerable.Range(0, 100).Select(i => Item($"clip {i} é.mp4", i)).ToArray());
            Assert.True(timeline.TabStop);
            Assert.True(InputKey(timeline, Keys.Left));
            Assert.True(InputKey(timeline, Keys.Control | Keys.Right));
            Assert.True(InputKey(timeline, Keys.Enter));
            Assert.False(InputKey(timeline, Keys.Tab));
            KeyDown(timeline, Keys.Right);
            Assert.Equal(0, timeline.SelectedIndex);
            for (var i = 1; i < 100; i++)
            {
                KeyDown(timeline, Keys.Right);
                Assert.Equal(i, timeline.SelectedIndex);
            }
            KeyDown(timeline, Keys.Right);
            Assert.Equal(99, timeline.SelectedIndex);
            KeyDown(timeline, Keys.Home);
            KeyDown(timeline, Keys.Left);
            Assert.Equal(0, timeline.SelectedIndex);
            KeyDown(timeline, Keys.End);
            Assert.Equal(99, timeline.SelectedIndex);
            Assert.Contains("Clip 100 of 100", timeline.AccessibleDescription);
            Assert.Contains(@"C:\missing\clip 99 é.mp4", timeline.AccessibleDescription);
        });
    }

    [Fact]
    public void Timeline_OnlyHandlesItsDocumentedCommands_AndDisabledControlCannotEdit()
    {
        StaTest.Run(() =>
        {
            using var timeline = new JoinVideosTimelineControl();
            timeline.SetItems([Item("a.mp4"), Item("b.mp4"), Item("c.mp4")]);
            timeline.SelectIndex(1);
            var moves = new List<(int, int)>();
            var removals = 0;
            timeline.MoveRequested += (_, e) => moves.Add((e.SourceIndex, e.InsertionIndex));
            timeline.RemoveRequested += (_, _) => removals++;
            KeyDown(timeline, Keys.Control | Keys.Left);
            KeyDown(timeline, Keys.Control | Keys.Right);
            Assert.Equal(new[] { (1, 0), (1, 3) }, moves);
            KeyDown(timeline, Keys.Delete);
            Assert.Equal(1, removals);
            Assert.False(KeyDown(timeline, Keys.Control | Keys.Delete).Handled);
            Assert.False(KeyDown(timeline, Keys.Shift | Keys.Right).Handled);
            timeline.SelectIndex(0);
            KeyDown(timeline, Keys.Control | Keys.Left);
            timeline.SelectIndex(2);
            KeyDown(timeline, Keys.Control | Keys.Right);
            Assert.Equal(2, moves.Count);
            timeline.Enabled = false;
            KeyDown(timeline, Keys.Delete);
            KeyDown(timeline, Keys.Control | Keys.Left);
            Assert.Equal(1, removals);
            Assert.Equal(2, moves.Count);
        });
    }

    [Theory]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Space)]
    public void Timeline_ActivationSelectsOnly_AndEmptyTimelineIsSafe(Keys key)
    {
        StaTest.Run(() =>
        {
            using var timeline = new JoinVideosTimelineControl();
            foreach (var command in new[] { key, Keys.Delete, Keys.Control | Keys.Left, Keys.End })
                Assert.True(KeyDown(timeline, command).SuppressKeyPress);
            Assert.Equal(-1, timeline.SelectedIndex);
            timeline.SetItems([Item("a.mp4"), Item("b.mp4")]);
            Assert.True(KeyDown(timeline, key).SuppressKeyPress);
            Assert.Equal(0, timeline.SelectedIndex);
            timeline.SelectIndex(1);
            KeyDown(timeline, key);
            Assert.Equal(1, timeline.SelectedIndex);
            var changes = 0;
            timeline.SelectedIndexChanged += (_, _) => changes++;
            timeline.SetItems([]);
            Assert.Equal(-1, timeline.SelectedIndex);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void Timeline_SelectionScrollsIntoItsOwnViewport_WithoutShowingAWindow()
    {
        StaTest.Run(() =>
        {
            using var host = new Form { ClientSize = new Size(320, 180) };
            var viewport = new Panel { Size = new Size(200, 120), AutoScroll = true };
            var timeline = new JoinVideosTimelineControl { Size = new Size(600, 182) };
            host.Controls.Add(viewport);
            viewport.Controls.Add(timeline);
            // Hidden parents suppress child-based extent measurement; declare the same virtual extent explicitly.
            viewport.AutoScrollMinSize = timeline.Size;
            _ = host.Handle;
            _ = viewport.Handle;
            _ = timeline.Handle;
            typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(bool)], null)!.Invoke(viewport, [true]);
            timeline.SetItems([Item("a.mp4"), Item("b.mp4"), Item("c.mp4")]);
            viewport.PerformLayout();
            KeyDown(timeline, Keys.End);
            Assert.True(viewport.AutoScrollPosition.X < 0,
                $"Client={viewport.ClientSize}, Display={viewport.DisplayRectangle}, Scroll={viewport.AutoScrollPosition}, HScroll={viewport.HorizontalScroll.Visible}, Timeline={timeline.Bounds}");
            KeyDown(timeline, Keys.Home);
            Assert.Equal(0, viewport.AutoScrollPosition.X);
            Assert.Equal(new Size(600, 182), timeline.Size);
            Assert.False(host.Visible);
        });
    }

    [Fact]
    public void Join_KeyboardReordersAndRemovesSelectedClip_WithoutStartingExport()
    {
        WithJoin(form =>
        {
            var timeline = Field<JoinVideosTimelineControl>(form, "_timeline");
            var items = Field<List<JoinVideoTimelineItem>>(form, "_items");
            items.AddRange([Item("a.mp4", 0), Item("b.mp4", 1), Item("c.mp4", 2)]);
            Call(form, "RefreshTimeline");
            timeline.SelectIndex(1);
            KeyDown(timeline, Keys.Control | Keys.Right);
            Assert.Equal(new[] { "a.mp4", "c.mp4", "b.mp4" }, form.TimelinePaths.Select(Path.GetFileName));
            Assert.Equal(2, timeline.SelectedIndex);
            Assert.Equal(4, form.OrderComboBox.SelectedIndex);
            KeyDown(timeline, Keys.Control | Keys.Left);
            Assert.Equal(new[] { "a.mp4", "b.mp4", "c.mp4" }, form.TimelinePaths.Select(Path.GetFileName));
            KeyDown(timeline, Keys.Enter);
            KeyDown(timeline, Keys.Space);
            Assert.Null(form.Settings);
            Assert.Equal(DialogResult.None, form.DialogResult);
            var removed = items[1];
            KeyDown(timeline, Keys.Delete);
            Assert.True(removed.IsRemoved);
            Assert.Equal(new[] { "a.mp4", "c.mp4" }, form.TimelinePaths.Select(Path.GetFileName));
            Assert.Equal(1, timeline.SelectedIndex);
            KeyDown(timeline, Keys.Delete);
            Assert.False(Field<Button>(form, "_joinButton").Enabled);
            Call(form, "ConfirmJoin");
            Assert.Null(form.Settings);
            Assert.False(form.Visible);
        });
    }

    [Fact]
    public void Join_FormPreviewDoesNotHijackKeysFromOtherControls_AndSortKeepsSelectedIdentity()
    {
        WithJoin(form =>
        {
            var timeline = Field<JoinVideosTimelineControl>(form, "_timeline");
            var items = Field<List<JoinVideoTimelineItem>>(form, "_items");
            items.AddRange([Item("z.mp4", 0), Item("b.mp4", 1), Item("a.mp4", 2)]);
            Call(form, "RefreshTimeline");
            timeline.SelectIndex(0);
            foreach (var key in new[] { Keys.Delete, Keys.Control | Keys.Left, Keys.Control | Keys.Right })
            {
                Assert.False(KeyDown(form, key).Handled);
                Assert.Equal(3, items.Count);
                Assert.Equal("z.mp4", Path.GetFileName(items[0].SourcePath));
            }
            form.OrderComboBox.SelectedIndex = 0;
            form.OrderComboBox.SelectedIndex = 1;
            Assert.Equal(new[] { "a.mp4", "b.mp4", "z.mp4" }, form.TimelinePaths.Select(Path.GetFileName));
            Assert.Equal(2, timeline.SelectedIndex);
            Assert.Contains("z.mp4", timeline.AccessibleDescription);
        });
    }

    [Fact]
    public void ActionFilter_RestoresLogicalFocusToTheNewChip_AndDoesNotInvokeAnAction()
    {
        StaTest.Run(() =>
        {
            using var host = new Form();
            var panel = new ActionsPanel();
            host.Controls.Add(panel);
            panel.SetFiles([@"C:\missing\a.mp4", @"C:\missing\b.wav"], []);
            var calls = 0;
            panel.ActionInvoked += (_, _) => calls++;
            var chip = Descendants(panel).OfType<Button>().Single(b => Equals(b.Tag, MediaFamily.Video));
            Call(chip, "OnClick", EventArgs.Empty);
            Assert.True(chip.IsDisposed);
            var replacement = Descendants(panel).OfType<Button>().Single(b => Equals(b.Tag, MediaFamily.Video));
            Assert.Same(replacement, panel.ActiveControl);
            Assert.Equal("Selected filter", replacement.AccessibleDescription);
            Assert.Equal(0, calls);
            Assert.False(host.Visible);
        });
    }

    [Fact]
    public void ActionRebuild_RestoresIdentityAcrossCaptionChanges_AndFallsBackWhenFilteredOut()
    {
        StaTest.Run(() =>
        {
            using var host = new Form();
            var panel = new ActionsPanel();
            host.Controls.Add(panel);
            panel.SetFiles([@"C:\missing\a.mp4", @"C:\missing\b.wav"], []);
            var chip = Descendants(panel).OfType<Button>().Single(b => Equals(b.Tag, MediaFamily.Video));
            FieldSet(panel, "_allFiles", new[] { @"C:\missing\a.mp4", @"C:\missing\b.mp4" });
            Call(panel, "Rebuild", chip);
            Assert.Equal("Video 2", panel.ActiveControl!.Text);
            var action = Descendants(panel).OfType<Button>().First(b => b.Enabled && b.Tag is ActionCatalogEntry);
            var key = ((ActionCatalogEntry)action.Tag!).Key;
            Call(panel, "Rebuild", action);
            Assert.Equal(key, ((ActionCatalogEntry)panel.ActiveControl!.Tag!).Key);
            action = (Button)panel.ActiveControl;
            FieldSet(panel, "_search", "no matching action 9382");
            Call(panel, "Rebuild", action);
            Assert.IsType<TextBox>(panel.ActiveControl);
            Assert.Equal("Search actions", panel.ActiveControl!.AccessibleName);
            Assert.False(host.Visible);
        });
    }

    [Fact]
    public void CommonShell_TabOrderKeepsMetadataBeforeFields_AndSecondaryBeforePrimary()
    {
        StaTest.Run(() =>
        {
            using var host = new Form();
            var header = NewHeader("Source details");
            var field = new TextBox();
            var close = FrameShiftUiFactory.CreateMeasuredActionButton("Close", false);
            var primary = FrameShiftUiFactory.CreateMeasuredActionButton("Start", true);
            var status = FrameShiftUiFactory.CreateStatusMessage("Status");
            var actions = FrameShiftDialogLayout.CreateActions(close, primary);
            var root = FrameShiftDialogLayout.Create(header,
                FrameShiftUiFactory.CreateFieldRow("&Start time", field), actions, status);
            host.Controls.Add(root);
            Assert.Same(header, root.GetNextControl(null, true));
            var body = root.GetControlFromPosition(0, 1)!;
            Assert.True(header.TabIndex < body.TabIndex && body.TabIndex < status.TabIndex && status.TabIndex < actions.TabIndex);
            Assert.Same(close, actions.GetNextControl(null, true));
            Assert.Same(primary, actions.GetNextControl(close, true));
            Assert.Same(close, actions.GetNextControl(primary, false));
            Assert.Equal("Start time", field.AccessibleName);
            primary.Enabled = false;
            var activations = 0;
            primary.Click += (_, _) => activations++;
            primary.PerformClick();
            Assert.Equal(0, activations);
        });
    }

    [Fact]
    public void NativeChoices_StayExclusive_AndSearchRebuildKeepsTextSelectionWithoutStealingFocus()
    {
        StaTest.Run(() =>
        {
            using var host = new Form();
            var first = new FrameShiftChoiceCard("Standard", "Default choice") { Checked = true };
            var second = new FrameShiftChoiceCard("Quality", "More detail");
            var choices = FrameShiftUiFactory.CreateChoiceRow(first, second);
            host.Controls.Add(choices);
            second.Checked = true;
            Assert.True(second.Checked);
            Assert.False(first.Checked);
            Assert.Same(first.Parent, second.Parent);
            Assert.Equal("More detail", second.AccessibleDescription);
            var panel = new ActionsPanel();
            host.Controls.Add(panel);
            panel.SetFiles([@"C:\missing\a.mp4"], []);
            var search = Descendants(panel).OfType<TextBox>().Single();
            search.Text = "Compress";
            search.Select(2, 3);
            Call(panel, "Rebuild", search);
            Assert.Same(search, panel.ActiveControl);
            Assert.Equal(2, search.SelectionStart);
            Assert.Equal(3, search.SelectionLength);
            var outside = new TextBox();
            host.Controls.Add(outside);
            host.ActiveControl = outside;
            panel.SetFiles([@"C:\missing\b.mp4"], []);
            Assert.Same(outside, host.ActiveControl);
            Assert.False(host.Visible);
        });
    }

    private static FrameShiftHeader NewHeader(string metadata) => new("FrameShift - Test", metadata, "", "", Point.Empty);
    [Fact]
    public void Queue_RemoveGlyphHasAnActionName_AndDeleteRemovesTheSelectedFile()
    {
        StaTest.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "FrameShift-F3-é " + Guid.NewGuid().ToString("N") + ".mp4");
            try
            {
                File.WriteAllText(path, "UI fixture only");
                using var host = new Form();
                var queue = new FileQueuePanel();
                host.Controls.Add(queue);
                Assert.Equal(1, queue.AddFiles([path]));
                var grid = Field<DataGridView>(queue, "_grid");
                Assert.Equal("Files queue", grid.AccessibleName);
                var remove = grid.Rows[0].Cells["Remove"];
                Assert.Equal($"Remove {Path.GetFileName(path)}", remove.AccessibilityObject.Name);
                Assert.Contains(Path.GetFileName(path), remove.ToolTipText);
                grid.Rows[0].Selected = true;
                KeyDown(grid, Keys.Delete);
                Assert.Empty(queue.Items);
                Assert.True(File.Exists(path));
                Assert.False(host.Visible);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Fact]
    public void Progress_RemoveGlyphExposesTheFileActionWithoutAddingAVisibleColumnCaption()
    {
        StaTest.Run(() =>
        {
            using var progress = new ProgressForm();
            progress.ReportQueue(["Vidéo avec accents.mp4"]);
            _ = progress.Handle;
            Application.DoEvents();
            var grid = Field<DataGridView>(progress, "_queueGrid");
            Assert.Equal("Task files queue", grid.AccessibleName);
            Assert.Equal("", grid.Columns["Remove"]!.HeaderText);
            Assert.Equal("Remove or cancel Vidéo avec accents.mp4", grid.Rows[0].Cells["Remove"].AccessibilityObject.Name);
            Assert.False(progress.Visible);
        });
    }

    private static JoinVideoTimelineItem Item(string name, int index = 0)
    {
        var video = new JoinVideoStreamInfo(null, null, null, null, 1920, 1080, null, null, null, null, null, null, null, null, null, null, null, 0);
        return new JoinVideoTimelineItem(@"C:\missing\" + name, index)
        {
            IsLoaded = true, Probe = new JoinVideoProbeResult(TimeSpan.FromSeconds(10), "mov,mp4", 1, 0, 0, video, null)
        };
    }

    private static void WithJoin(Action<JoinVideosForm> action) => StaTest.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "FrameShift-F3-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var logger = new AppLogger();
            using var form = new JoinVideosForm([], "missing-ffmpeg", "missing-ffprobe",
                new FfmpegRunner(logger), new FfprobeRunner(logger), uiSettingsPathForTesting: path);
            action(form);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    });

    private static KeyEventArgs KeyDown(Control control, Keys key)
    {
        var args = new KeyEventArgs(key);
        Call(control, "OnKeyDown", args);
        return args;
    }
    private static bool InputKey(Control control, Keys key) => (bool)Call(control, "IsInputKey", key)!;
    private static bool DialogKey(Control control, Keys key) => (bool)Call(control, "ProcessDialogKey", key)!;
    private static bool CommandKey(Control control, Keys key) => (bool)Call(control, "ProcessCmdKey", new Message(), key)!;
    private static object? Call(object instance, string method, params object?[] args)
        => instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static T Field<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void FieldSet(object instance, string name, object value)
        => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
