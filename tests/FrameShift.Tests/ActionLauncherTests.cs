using System;
using System.Linq;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

// Guards for the child-process command builder used by the main window. The launch
// itself is fire-and-forget and not unit-tested; everything up to ProcessStartInfo is.
public sealed class ActionLauncherTests
{
    private static ActionCatalogEntry Entry(string key)
    {
        Assert.True(ActionCatalog.TryGet(key, out var entry));
        return entry;
    }

    [Fact]
    public void BuildArguments_StartsWithActionFlagAndId()
    {
        var args = ActionLauncher.BuildArguments(Entry("convert-video"), new[] { @"C:\a.mp4" });

        Assert.Equal("--action", args[0]);
        Assert.Equal("convert-video", args[1]);
    }

    [Fact]
    public void BuildArguments_AppendsPaths_AfterActionId_InOrder()
    {
        var paths = new[] { @"C:\a.mp4", @"C:\b.mkv" };

        var args = ActionLauncher.BuildArguments(Entry("convert-video"), paths);

        Assert.Equal(new[] { "--action", "convert-video", @"C:\a.mp4", @"C:\b.mkv" }, args);
    }

    [Fact]
    public void BuildArguments_RemoveBackgroundVariant_InjectsModelFlag_BeforePaths()
    {
        var args = ActionLauncher.BuildArguments(Entry("remove-background-high-resolution"), new[] { @"C:\img.png" });

        Assert.Equal(
            new[] { "--action", "remove-background", "--rmbg-model", "high-resolution", @"C:\img.png" },
            args);
    }

    [Fact]
    public void BuildArguments_NeverIncludesTargetOrProfile()
    {
        foreach (var entry in ActionCatalog.Entries)
        {
            var args = ActionLauncher.BuildArguments(entry, new[] { "C:\\file" + entry.AcceptedExtensions[0] });

            Assert.DoesNotContain("--target", args);
            Assert.DoesNotContain("--profile", args);
        }
    }

    [Fact]
    public void BuildArguments_PreservesSpacesAndAccents_AsSingleArgumentEach()
    {
        var paths = new[] { @"E:\vidéos\mon clip final.mp4", @"C:\dossier accentué\imagé (1).mp4" };

        var args = ActionLauncher.BuildArguments(Entry("convert-video"), paths);

        Assert.Contains(@"E:\vidéos\mon clip final.mp4", args);
        Assert.Contains(@"C:\dossier accentué\imagé (1).mp4", args);
    }

    [Fact]
    public void BuildArguments_ThrowsOnEmptyPaths()
    {
        Assert.Throws<ArgumentException>(
            () => ActionLauncher.BuildArguments(Entry("convert-video"), Array.Empty<string>()));
    }

    [Fact]
    public void BuildArguments_ThrowsOnBlankPath()
    {
        Assert.Throws<ArgumentException>(
            () => ActionLauncher.BuildArguments(Entry("convert-video"), new[] { "   " }));
    }

    [Fact]
    public void CreateStartInfo_ConfiguresProcessCorrectly()
    {
        var paths = new[] { @"E:\vidéos\a b.png" };

        var startInfo = ActionLauncher.CreateStartInfo(
            Entry("remove-background-fast"),
            paths,
            @"C:\Program Files\FrameShift\FrameShift.exe");

        Assert.Equal(@"C:\Program Files\FrameShift\FrameShift.exe", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            new[] { "--action", "remove-background", "--rmbg-model", "fast", @"E:\vidéos\a b.png" },
            startInfo.ArgumentList.ToArray());
    }

    [Fact]
    public void CreateStartInfo_ArgumentList_KeepsPathVerbatim_NoManualQuoting()
    {
        var startInfo = ActionLauncher.CreateStartInfo(Entry("convert-video"), new[] { @"C:\a b\c.mp4" }, "FrameShift.exe");

        var lastArg = startInfo.ArgumentList.Last();
        Assert.Equal(@"C:\a b\c.mp4", lastArg);
        Assert.DoesNotContain('"', lastArg);
    }

    [Fact]
    public void CreateStartInfo_ThrowsOnBlankExecutablePath()
    {
        Assert.Throws<ArgumentException>(
            () => ActionLauncher.CreateStartInfo(Entry("convert-video"), new[] { @"C:\a.mp4" }, "  "));
    }
}
