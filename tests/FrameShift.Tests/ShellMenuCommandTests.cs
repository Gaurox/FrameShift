using FrameShift.Core.Actions;
using FrameShift.ShellMenu;
using System.Text.RegularExpressions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ShellMenuCommandTests
{
    [Fact]
    public void NativeActions_MatchInstallerGroupsAndFixedModelArguments()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "installer", "FrameShift.iss"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(directory.FullName, "installer", "FrameShift.iss"));
        var declarations = Regex.Matches(script,
            @"Apply(?<ai>AI)?ActionMenuList\(\s*(?<extensions>\w+(?: \+ ',\.pdf')?),\s*'(?<key>[^']+)',\s*CustomMessage\('[^']+'\),\s*'(?<action>[^']+)',\s*'(?<suffix>[^']*)'\s*\)");
        foreach (var command in MenuCommands.All.Where(command => command.FrameMode is null && command.Action != "join-videos"))
        {
            var matches = declarations.Where(match => match.Groups["key"].Value == command.Key).ToArray();
            Assert.NotEmpty(matches);
            foreach (var match in matches)
            {
                Assert.Equal(command.Action, match.Groups["action"].Value);
                Assert.Equal(command.Ai, match.Groups["ai"].Success);
                Assert.Equal(command.Model is null ? "" : " --rmbg-model " + command.Model, match.Groups["suffix"].Value);
            }
            var registered = matches.SelectMany(match => match.Groups["extensions"].Value switch
            {
                "VideoExtensions" => MenuCommands.VideoExtensions,
                "AudioExtensions" => MenuCommands.AudioExtensions,
                "ImageExtensions" or "PdfImageExtensions" => MenuCommands.ImageExtensions,
                "ImageExtensions + ',.pdf'" => [.. MenuCommands.ImageExtensions, ".pdf"],
                _ => throw new InvalidOperationException("Unknown installer extension set.")
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(MenuCommands.Extensions.Where(command.Accepts), extension => Assert.Contains(extension, registered));
        }
    }

    [Fact]
    public void ShellCommands_MatchPublishedActionArities_AndRetainCutVideoLoopEntry()
    {
        Assert.Equal(47, MenuCommands.All.Length);
        Assert.Equal(47, MenuCommands.All.Select(command => command.Verb).Distinct().Count());
        foreach (var command in MenuCommands.All)
        {
            var catalogKey = command.Model is null ? command.Action : command.Action + "-" + command.Model;
            Assert.True(ActionCatalog.TryGet(catalogKey, out var entry), catalogKey);
            Assert.Equal(entry.Arity == ActionArity.Single, command.Maximum == 1);
            Assert.Equal(entry.MinimumInputCount, command.Minimum);
            Assert.Equal(entry.AcceptedExtensions.Order(), MenuCommands.Extensions.Where(command.Accepts).Order());
            var extension = entry.AcceptedExtensions[0];
            var file = @"C:\Essais été 中文\source" + extension;
            var paths = Enumerable.Repeat(file, command.Minimum).ToArray();
            var commandLaunch = MenuCommands.BuildLaunch(@"C:\FrameShift\FrameShift.exe", command, paths);
            var extra = command.FrameMode is null ? entry.ExtraCliArgs : new[] { "--frame-mode", command.FrameMode };
            Assert.Equal(new[] { "--action", command.Action }.Concat(extra).Concat(paths), commandLaunch.ArgumentList);
        }
        var cut = Assert.Single(MenuCommands.All, command => command.Action == "cut-video");
        var launch = MenuCommands.BuildLaunch(@"C:\FrameShift\FrameShift.exe", cut, [@"C:\Vidéos été\clip.mp4"]);
        Assert.Equal(new[] { "--action", "cut-video", @"C:\Vidéos été\clip.mp4" }, launch.ArgumentList);
        Assert.False(launch.UseShellExecute);
        Assert.True(launch.CreateNoWindow);
    }

    [Theory]
    [InlineData("convert-audio", ".aac")]
    [InlineData("separate-audio", ".wave")]
    [InlineData("remove-noise", ".wma")]
    [InlineData("resize-video", ".png")]
    [InlineData("extract-text", ".mp4")]
    public void Commands_RejectUnsupportedInputs(string action, string extension)
    {
        var command = Assert.Single(MenuCommands.All, command => command.Action == action);
        Assert.Throws<ArgumentException>(() => MenuCommands.BuildLaunch(@"C:\FrameShift\FrameShift.exe", command, [@"C:\Source" + extension]));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("first")]
    [InlineData("last")]
    [InlineData("keyframes")]
    public void FrameModes_UseExistingCliValues(string mode)
    {
        var command = Assert.Single(MenuCommands.All, command => command.FrameMode == mode);
        var launch = MenuCommands.BuildLaunch(@"C:\FrameShift\FrameShift.exe", command, [@"C:\Vidéo.mp4"]);
        Assert.Equal(new[] { "--action", "extract-frames", "--frame-mode", mode, @"C:\Vidéo.mp4" }, launch.ArgumentList);
    }
}
