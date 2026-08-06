using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed class FileQueuePanelTests
{
    [Fact]
    public void AddFiles_WithoutUserSelection_LeavesTheWholeQueueUnselected()
    {
        StaTest.Run(() =>
        {
            using var files = new TemporaryFiles("first.mp4", "second.mp3");
            using var panel = new FileQueuePanel();

            Assert.Equal(2, panel.AddFiles(files.Paths));
            Assert.Equal(2, panel.Items.Count);
            Assert.Empty(panel.SelectedPaths);
        });
    }

    [Fact]
    public void AddFiles_PreservesAnExplicitSelection()
    {
        StaTest.Run(() =>
        {
            using var files = new TemporaryFiles("first.mp4", "second.mp3", "third.png");
            using var panel = new FileQueuePanel();
            panel.AddFiles(files.Paths[..2]);

            var grid = GetGrid(panel);
            grid.Rows[1].Selected = true;
            Assert.Equal(new[] { files.Paths[1] }, panel.SelectedPaths);

            Assert.Equal(1, panel.AddFiles(new[] { files.Paths[2] }));
            Assert.Equal(new[] { files.Paths[1] }, panel.SelectedPaths);
        });
    }

    private static DataGridView GetGrid(FileQueuePanel panel)
    {
        var field = typeof(FileQueuePanel).GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<DataGridView>(field?.GetValue(panel));
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"FrameShift.Tests.{Guid.NewGuid():N}");

        public TemporaryFiles(params string[] fileNames)
        {
            Directory.CreateDirectory(_directory);
            Paths = fileNames.Select(fileName => Path.Combine(_directory, fileName)).ToArray();
            foreach (var path in Paths)
            {
                File.WriteAllText(path, string.Empty);
            }
        }

        public string[] Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
