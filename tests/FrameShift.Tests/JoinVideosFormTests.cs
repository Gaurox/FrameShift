using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.FFprobe;
using FrameShift.Core.Logging;
using FrameShift.Windows.Forms;
using Xunit;

namespace FrameShift.Tests;

public sealed class JoinVideosFormTests
{
    [Fact]
    public void ExplorerArrivals_BeforeAndAfterHandle_AreAllAddedInReceivedOrder()
    {
        StaTest.Run(() =>
        {
            var settingsPath = CreateTempSettingsPath();
            try
            {
                var logger = new AppLogger();
                using var form = new JoinVideosForm(
                    [],
                    "missing-ffmpeg.exe",
                    "missing-ffprobe.exe",
                    new FfmpegRunner(logger),
                    new FfprobeRunner(logger),
                    uiSettingsPathForTesting: settingsPath);
                form.OrderComboBox.SelectedIndex = 0;

                var first = @"C:\missing\first.mp4";
                var second = @"C:\missing\second.mp4";
                form.AddPathsThreadSafe([first]);
                form.Show();
                Application.DoEvents();
                Assert.False(form.IsClosingForTesting);
                Assert.False(form.IsDisposed);
                PumpMessagesUntil(() => form.TimelinePaths.Count == 1);
                Assert.Equal(new[] { first }, form.TimelinePaths);

                var sender = new Thread(() => form.AddPathsThreadSafe([second, first]));
                sender.Start();
                sender.Join();

                PumpMessagesUntil(() => form.TimelinePaths.Count == 3);

                Assert.Equal(new[] { first, second, first }, form.TimelinePaths);
                form.Close();
            }
            finally
            {
                DeleteIfExists(settingsPath);
            }
        });
    }

    [Fact]
    public void AddVideosButton_AddsEveryPickerOccurrenceToTimeline()
    {
        StaTest.Run(() =>
        {
            var settingsPath = CreateTempSettingsPath();
            try
            {
                var logger = new AppLogger();
                var selectedPaths = new[]
                {
                    @"C:\missing\clip 2.mp4",
                    @"C:\missing\clip 10.mp4",
                    @"C:\missing\clip 2.mp4"
                };
                var pickerCalled = false;
                using var form = new JoinVideosForm(
                    [],
                    "missing-ffmpeg.exe",
                    "missing-ffprobe.exe",
                    new FfmpegRunner(logger),
                    new FfprobeRunner(logger),
                    _ =>
                    {
                        pickerCalled = true;
                        return selectedPaths;
                    },
                    uiSettingsPathForTesting: settingsPath);
                form.OrderComboBox.SelectedIndex = 0;

                form.Show();
                Application.DoEvents();
                Assert.False(form.IsClosingForTesting);
                Assert.False(form.IsDisposed);

                Assert.True(form.AddVideosButton.Visible);
                Assert.True(form.AddVideosButton.Enabled);
                Assert.True(form.AddVideosButton.CanSelect);
                var clickRaised = false;
                form.AddVideosButton.Click += (_, _) => clickRaised = true;
                form.AddVideosButton.PerformClick();
                Application.DoEvents();

                Assert.True(clickRaised);
                Assert.True(pickerCalled);
                Assert.Equal(selectedPaths, form.TimelinePaths);
                form.Close();
            }
            finally
            {
                DeleteIfExists(settingsPath);
            }
        });
    }

    [Fact]
    public void ResolveSortOrderIndex_NoSavedPreference_DefaultsToNaturalFileName()
    {
        Assert.Equal(1, JoinVideosForm.ResolveSortOrderIndex(null));
        Assert.Equal(1, JoinVideosForm.ResolveSortOrderIndex(""));
        Assert.Equal(1, JoinVideosForm.ResolveSortOrderIndex("not-a-real-key"));
    }

    [Fact]
    public void ResolveSortOrderIndex_SavedPreference_RoundTripsThroughGetSortOrderKey()
    {
        foreach (var index in new[] { 0, 1, 2, 3 })
        {
            var key = JoinVideosForm.GetSortOrderKey(index);
            Assert.NotNull(key);
            Assert.Equal(index, JoinVideosForm.ResolveSortOrderIndex(key));
        }
    }

    [Fact]
    public void GetSortOrderKey_CustomOrInvalidIndex_ReturnsNull()
    {
        Assert.Null(JoinVideosForm.GetSortOrderKey(4));
        Assert.Null(JoinVideosForm.GetSortOrderKey(-1));
    }

    private static string CreateTempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"frameshift-joinvideos-tests_{Guid.NewGuid():N}.json");

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void PumpMessagesUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Yield();
        }

        Assert.True(condition(), "The marshalled timeline update did not reach the WinForms message loop.");
    }
}
