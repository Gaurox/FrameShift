using FrameShift.Core.Helpers;
using Xunit;

namespace FrameShift.Tests;

public sealed class OutputOperationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"frameshift é {Guid.NewGuid():N}");
    public OutputOperationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionAfterWritingPreservesForeignFileOrDirectory(bool directory)
    {
        var desired = Path.Combine(_root, "résultat.png");
        using var output = OutputOperation.ForFile(desired);
        File.WriteAllText(output.WorkingPath, "generated");
        if (directory) Directory.CreateDirectory(desired);
        else File.WriteAllText(desired, "sentinel");
        var final = output.Publish(CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "résultat_001.png"), final);
        Assert.Equal("generated", File.ReadAllText(final));
        if (directory) Assert.True(Directory.Exists(desired));
        else Assert.Equal("sentinel", File.ReadAllText(desired));
    }

    [Fact]
    public async Task ConcurrentPublishersKeepBothFiles()
    {
        var desired = Path.Combine(_root, "résultat.wav");
        using var first = OutputOperation.ForFile(desired);
        using var second = OutputOperation.ForFile(desired);
        File.WriteAllText(first.WorkingPath, "first");
        File.WriteAllText(second.WorkingPath, "second");
        using var barrier = new Barrier(2);
        var results = await Task.WhenAll(new[] { first, second }.Select(operation => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return operation.Publish(CancellationToken.None);
        })));
        Assert.NotEqual(results[0], results[1]);
        Assert.Equal("first", File.ReadAllText(results[0]));
        Assert.Equal("second", File.ReadAllText(results[1]));
    }

    [Fact]
    public void CancellationCleansOnlyWorkspace()
    {
        var desired = Path.Combine(_root, "résultat.png");
        File.WriteAllText(desired, "sentinel");
        string workspace;
        using (var output = OutputOperation.ForFile(desired))
        {
            workspace = output.WorkspacePath;
            File.WriteAllText(output.WorkingPath, "partial");
            Assert.Throws<OperationCanceledException>(() => output.Publish(new CancellationToken(true)));
        }
        Assert.False(Directory.Exists(workspace));
        Assert.Equal("sentinel", File.ReadAllText(desired));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void PublishedDirectorySurvivesDisposeAndContainsOnlyPayload()
    {
        var desired = Path.Combine(_root, "frames");
        using (var output = OutputOperation.ForDirectory(desired))
        {
            Directory.CreateDirectory(output.WorkingPath);
            File.WriteAllText(Path.Combine(output.WorkingPath, "0001.png"), "image");
            File.WriteAllText(Path.Combine(output.WorkspacePath, "scratch"), "private");
            Directory.CreateDirectory(desired);
            File.WriteAllText(Path.Combine(desired, "sentinel"), "foreign");
            Assert.Equal(desired + "_001", output.Publish(CancellationToken.None));
        }
        Assert.Equal("foreign", File.ReadAllText(Path.Combine(desired, "sentinel")));
        Assert.Single(Directory.GetFiles(desired + "_001"));
        Assert.Empty(Directory.GetDirectories(_root, ".frameshift-*"));
    }

    [Fact]
    public void LockedWorkingFileFailsWithoutRetryOrRemovingExistingOutput()
    {
        var desired = Path.Combine(_root, "result.wav");
        File.WriteAllText(desired, "sentinel");
        using var output = OutputOperation.ForFile(desired);
        using (var writer = new FileStream(output.WorkingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            Assert.Throws<IOException>(() => output.Publish(CancellationToken.None));
        Assert.Null(output.PublishedPath);
        Assert.False(File.Exists(Path.Combine(_root, "result_001.wav")));
        Assert.Equal("sentinel", File.ReadAllText(desired));
    }
}
