using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameShift.Core.Actions;
using FrameShift.Core.FFmpeg;
using FrameShift.Core.Helpers;
using FrameShift.Core.Logging;
using Xunit;

namespace FrameShift.Tests;

[Collection(WinFormsTestCollection.Name)]
public sealed class VideoLoopResourceLifecycleTests
{
    [Fact]
    public async Task UnconfirmedPreviewStop_RetainsItsBundleUntilTheProcessHasStopped()
    {
        using var fixture = new VideoLoopExportFixture(); await fixture.GenerateAsync();
        using var cancel = new CancellationTokenSource();
        var pid = 0;
        var runner = new FfmpegRunner(new AppLogger(), process =>
        {
            if (Interlocked.CompareExchange(ref pid, process.Id, 0) == 0) NtSuspendProcess(process.Handle);
            throw new Win32Exception(5);
        });
        VideoLoopProcessTerminationException? retained = null;
        string? bundle = null;
        try
        {
            var assembling = false;
            var progress = new LoopExportTestProgress((_, _) => { if (assembling) cancel.Cancel(); },
                text => { if (text?.StartsWith("Assembling") == true) assembling = true; });
            retained = await Assert.ThrowsAsync<VideoLoopProcessTerminationException>(() => new VideoLoopPreviewBuilder(runner, fixture.Prober)
                .BuildAsync(fixture.Tools.ResolveFfmpegPath(), fixture.Tools.ResolveFfprobePath(), fixture.Source, fixture.Probe,
                    new(1, 20, 10), new(VideoLoopMode.Natural, KeepAudio: false), new(0, 2, 0), VideoLoopPreviewKind.Junction, progress, cancel.Token)
                .WaitAsync(TimeSpan.FromSeconds(30)));
            bundle = Path.GetDirectoryName(retained.Destination)!;
            Assert.True(Directory.Exists(bundle)); Assert.True(File.Exists(retained.Destination));
            Assert.True(Directory.Exists(retained.Workspace));
        }
        finally
        {
            if (pid != 0)
            {
                try { using var process = Process.GetProcessById(pid); process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (ArgumentException) { }
            }
            if (retained is not null) Directory.Delete(retained.Workspace, true);
            if (bundle is not null)
            {
                Assert.StartsWith("frameshift_loop_preview_", Path.GetFileName(bundle));
                Directory.Delete(bundle, true);
            }
        }
    }

    [Fact]
    public async Task DiskSpaceLostDuringEncoding_StopsTheProcess_AndReportsAResourceFailure()
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync("testsrc2=s=1920x1080:r=10", 12);
        var full = 0;
        var encoding = false;
        var progress = new LoopExportTestProgress((_, _) => { if (encoding) Interlocked.Exchange(ref full, 1); },
            text => { if (text?.StartsWith("Encoding") == true) encoding = true; });
        var exporter = new VideoLoopExporter(fixture.Runner, fixture.Prober)
        { AvailableSpace = _ => Volatile.Read(ref full) == 0 ? long.MaxValue : 0 };
        var output = Path.Combine(fixture.Root, "disk lost.mp4");
        var error = await Assert.ThrowsAsync<IOException>(() => fixture.RenderAsync(output, new(VideoLoopMode.Natural), new(0, 12, 0), progress, exporter: exporter));
        Assert.Contains("not enough disk space", error.Message);
        Assert.False(File.Exists(output));
        using var exclusive = File.Open(fixture.Source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOsStop_RetainsRenderAndOutputWorkspaces_UntilTheOwnedProcessIsKilled(bool atEncoding)
    {
        using var fixture = new VideoLoopExportFixture();
        await fixture.GenerateAsync("testsrc2=s=1920x1080:r=10", 12);
        using var cancel = new CancellationTokenSource();
        var pid = 0; var suspendResult = -1;
        var runner = new FfmpegRunner(new AppLogger(), process =>
        {
            if (Interlocked.CompareExchange(ref pid, process.Id, 0) == 0)
                suspendResult = NtSuspendProcess(process.Handle);
            throw new Win32Exception(5); // Exercise the runner's real two-stop deadline.
        });
        VideoLoopProcessTerminationException? retained = null;
        try
        {
            var encoding = false;
            var progress = new LoopExportTestProgress((_, _) => { if (!atEncoding || encoding) cancel.Cancel(); },
                text => { if (text?.StartsWith("Encoding") == true) encoding = true; });
            var request = new ActionRequest(fixture.Source, new AppLogger(), progress);
            retained = await Assert.ThrowsAsync<VideoLoopProcessTerminationException>(() => new VideoLoopExporter(runner, fixture.Prober)
                .ExportAsync(fixture.Tools.ResolveFfmpegPath(), fixture.Tools.ResolveFfprobePath(), request, fixture.Probe,
                    new(1, 120, 10), new(VideoLoopMode.Natural), new(0, 12, 0), cancel.Token).WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(0, suspendResult);
            Assert.True(Directory.Exists(retained.Workspace));
            Assert.Single(Directory.GetDirectories(fixture.Root, ".frameshift-*"));
            Assert.False(File.Exists(OutputPathHelper.GetOutputPath(fixture.Source, "_loop", ".mp4")));
            using var process = Process.GetProcessById(pid);
            Assert.False(process.HasExited);
        }
        finally
        {
            if (pid != 0)
            {
                try { using var process = Process.GetProcessById(pid); process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (ArgumentException) { }
            }
            if (retained is not null)
            {
                Assert.StartsWith("frameshift_loop_render_", Path.GetFileName(retained.Workspace));
                Directory.Delete(retained.Workspace, true);
            }
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr handle);
}
