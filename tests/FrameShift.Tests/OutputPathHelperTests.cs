using System;
using System.IO;
using FrameShift.Core.Helpers;
using Xunit;

namespace FrameShift.Tests;

public sealed class OutputPathHelperTests
{
    [Fact]
    public void CreateUniqueOutputDirectoryPath_ReturnsBaseFolderWhenUnused()
    {
        var root = CreateTempDirectory();
        try
        {
            var inputPath = Path.Combine(root, "Video Name.mp4");
            File.WriteAllText(inputPath, "x");

            var outputPath = OutputPathHelper.CreateUniqueOutputDirectoryPath(inputPath, "_frames");

            Assert.Equal(Path.Combine(root, "Video Name_frames"), outputPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateUniqueOutputDirectoryPath_AppendsNumericSuffixWhenFolderExists()
    {
        var root = CreateTempDirectory();
        try
        {
            var inputPath = Path.Combine(root, "Video Name.mp4");
            File.WriteAllText(inputPath, "x");
            Directory.CreateDirectory(Path.Combine(root, "Video Name_frames"));

            var outputPath = OutputPathHelper.CreateUniqueOutputDirectoryPath(inputPath, "_frames");

            Assert.Equal(Path.Combine(root, "Video Name_frames_001"), outputPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateUniqueDirectoryPath_AppendsNumericSuffixWhenFileBlocksBaseName()
    {
        var root = CreateTempDirectory();
        try
        {
            var desiredPath = Path.Combine(root, "Video_frames");
            File.WriteAllText(desiredPath, "x");

            var outputPath = OutputPathHelper.CreateUniqueDirectoryPath(desiredPath);

            Assert.Equal(Path.Combine(root, "Video_frames_001"), outputPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"frameshift_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
