using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FrameShift.Core.AI;
using FrameShift.Core.AI.Ocr;
using Xunit;

namespace FrameShift.Tests;

public sealed class AiModelPathSafetyTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "FrameShift chemins modèles é " + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];

    public AiModelPathSafetyTests() => Directory.CreateDirectory(_temp);

    [Theory]
    [InlineData("above")]
    [InlineData("root")]
    [InlineData("child")]
    public void JunctionsAreRejectedBeforeCreationOrCleanup(string location)
    {
        var foreign = Path.Combine(_temp, "sentinelle");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "keep.txt"), "untouched");
        var link = Path.Combine(_temp, "link");
        Junction(link, foreign);
        var root = location == "above" ? Path.Combine(link, "models") : location == "root" ? link : Path.Combine(_temp, "models");
        var child = Path.Combine(root, "rife");
        if (location == "child")
        {
            Directory.CreateDirectory(root);
            Junction(child, foreign);
        }

        Assert.Throws<InvalidOperationException>(() => AiModelStorage.EnsureDirectory(root, child));
        Assert.False(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, child));
        Assert.Throws<InvalidOperationException>(() => OcrModelDirectorySafety.ValidateDownloadPath(root, Path.Combine(child, "model.onnx")));
        Assert.False(AiModelStorage.HasOwnershipMarker(child));
        Assert.Equal(new[] { "keep.txt" }, Directory.GetFileSystemEntries(foreign).Select(Path.GetFileName));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(foreign, "keep.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomRootRejectsJunctionAboveOrAtRoot(bool above)
    {
        var target = Path.Combine(_temp, "foreign");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_temp, "alias");
        Junction(link, target);
        Assert.False(AiModelDirectorySafety.TryNormalizeCustomDirectory(above ? Path.Combine(link, "models") : link, out _));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Theory]
    [InlineData("relative\\model.onnx")]
    [InlineData(@"C:\models\model.onnx:stream")]
    [InlineData(@"C:\models \model.onnx")]
    [InlineData(@"C:\models\file.")]
    [InlineData(@"\\?\C:\models\file")]
    [InlineData(@"\\.\C:\models\file")]
    public void AmbiguousAndDevicePathsAreRejected(string path) =>
        Assert.Throws<InvalidOperationException>(() => AiModelPathLease.Normalize(path));

    [Fact]
    public void ParentAndRootStayPinnedUntilLeaseIsDisposed()
    {
        var parent = Path.Combine(_temp, "parent");
        var root = Path.Combine(parent, "models");
        using (AiModelPathLease.Acquire(root, create: true))
        {
            Assert.Throws<IOException>(() => Directory.Move(parent, parent + " renamed"));
            Assert.Throws<IOException>(() => Directory.Move(root, root + " renamed"));
        }
        Directory.Move(parent, parent + " renamed");
        Assert.True(Directory.Exists(parent + " renamed"));
    }

    [Fact]
    public void ReparseEntriesPreventOwnershipCleanupEvenWithValidMarker()
    {
        var root = Path.Combine(_temp, "models");
        var model = Path.Combine(root, "rife");
        AiModelStorage.EnsureDirectory(root, model);
        var target = Path.Combine(_temp, "foreign");
        Directory.CreateDirectory(target);
        Junction(Path.Combine(model, "rife_v426_x2.onnx"), target);
        Assert.False(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, model));
    }

    [Fact]
    public void KnownLockFilesAreOwnedButUnknownTemporaryFilesArePreserved()
    {
        var root = Path.Combine(_temp, "models");
        var model = Path.Combine(root, "rife");
        AiModelStorage.EnsureDirectory(root, model);
        File.WriteAllText(Path.Combine(model, "rife_v426_x2.onnx.download-lock"), "");
        Assert.True(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, model));
        File.WriteAllText(Path.Combine(model, ".unknown.tmp"), "foreign");
        Assert.False(AiModelStorage.CanDeleteOwnedKnownModelDirectory(root, model));
    }

    [Fact]
    public void LiveGuardPreventsTurningThePinnedDirectoryIntoAJunction()
    {
        var foreign = Path.Combine(_temp, "foreign");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "sentinel"), "unchanged");
        var template = Path.Combine(_temp, "template junction");
        Junction(template, foreign);
        var data = new byte[16384];
        using var templateHandle = AiModelPathLease.Open(template, 0x80000000, FileShare.ReadWrite | FileShare.Delete, 3, 0x02000000);
        Assert.True(DeviceIoControl(templateHandle, 0x000900A8, null, 0, data, data.Length, out var size, IntPtr.Zero));
        var models = Path.Combine(_temp, "regular");
        using (AiModelPathLease.Acquire(models, create: true, allowFileUpdates: true))
        {
            using var writer = AiModelPathLease.Open(models, 0x40000000, FileShare.ReadWrite | FileShare.Delete, 3, 0x02000000);
            Assert.False(DeviceIoControl(writer, 0x000900A4, data, size, null, 0, out _, IntPtr.Zero));
            Assert.Equal(145, Marshal.GetLastWin32Error()); // ERROR_DIR_NOT_EMPTY
            Assert.Throws<IOException>(() => Directory.Move(models, models + " moved"));
        }
        Assert.Empty(Directory.GetFileSystemEntries(models));
        Assert.False(File.GetAttributes(models).HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(foreign, "sentinel")));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[]? input, int inputSize,
        byte[]? output, int outputSize, out int returned, IntPtr overlapped);

    internal static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10000), "Junction creation timed out.");
        Assert.True(process.ExitCode == 0, stdout + stderr);
    }

    private void Junction(string link, string target)
    {
        CreateJunction(link, target);
        _junctions.Add(link);
    }

    public void Dispose()
    {
        foreach (var link in _junctions.AsEnumerable().Reverse()) if (Directory.Exists(link)) Directory.Delete(link);
        Directory.Delete(_temp, true);
    }
}
