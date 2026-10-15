using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class FileQueuePathExpanderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"FrameShift dépôt été {Guid.NewGuid():N}");
    public FileQueuePathExpanderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ExpansionIsShallowKeepsAllExtensionsAndPreservesExplicitPathOrder()
    {
        var file = Path.Combine(_root, "fichier é.mp4");
        var other = Path.Combine(_root, "notes.txt");
        File.WriteAllText(file, ""); File.WriteAllText(other, "");
        var nested = Directory.CreateDirectory(Path.Combine(_root, "sous dossier")).FullName;
        File.WriteAllText(Path.Combine(nested, "nested.mp4"), "");
        var actual = FileQueuePathExpander.Enumerate([other, _root, file], CancellationToken.None).ToArray();
        Assert.Equal(other, actual[0]);
        Assert.Equal(file, actual[^1]);
        Assert.Equal(Directory.GetFiles(_root), actual[1..^1]);
        Assert.DoesNotContain(actual, path => path.Contains("nested.mp4"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnreadableFolderIsReportedAndDoesNotPreventFollowingFiles(bool duringEnumeration)
    {
        var readable = Path.Combine(_root, "lisible.png");
        File.WriteAllText(readable, "");
        var errors = new List<string>();
        IEnumerable<string> BrokenDirectory(string _)
        {
            if (duringEnumeration) yield return readable;
            throw new UnauthorizedAccessException("Access denied fixture");
        }
        var actual = FileQueuePathExpander.Enumerate([_root, readable], CancellationToken.None, errors.Add,
            duringEnumeration ? BrokenDirectory : _ => throw new UnauthorizedAccessException("Access denied fixture")).ToArray();
        Assert.Equal(duringEnumeration ? new[] { readable, readable } : new[] { readable }, actual);
        Assert.Single(errors);
        Assert.Contains("Access denied", errors[0]);
    }

    [Fact]
    public void MissingBlankAndInvalidPathsDoNotStopFollowingFiles()
    {
        var readable = Path.Combine(_root, "valid.wav"); File.WriteAllText(readable, "");
        var errors = new List<string>();
        Assert.Equal(new[] { readable }, FileQueuePathExpander.Enumerate(
            [null!, "", "  ", "bad\0path", Path.Combine(_root, "missing"), readable], CancellationToken.None, errors.Add));
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void CancellationStopsTheIteratorBeforeReadingTheNextPath()
    {
        var file = Path.Combine(_root, "valid.wav"); File.WriteAllText(file, "");
        using var cancellation = new CancellationTokenSource();
        using var iterator = FileQueuePathExpander.Enumerate([file, file], cancellation.Token).GetEnumerator();
        Assert.True(iterator.MoveNext());
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => iterator.MoveNext());
    }

    [Fact]
    public void AFileThatDisappearedDuringEnumerationIsSkipped()
    {
        var file = Path.Combine(_root, "valid.wav"); File.WriteAllText(file, "");
        var vanished = Path.Combine(_root, "removed.wav");
        Assert.Equal(new[] { file }, FileQueuePathExpander.Enumerate([_root], CancellationToken.None,
            enumerateDirectory: _ => new[] { vanished, file }));
    }

    [Fact]
    public void LongLocalPathsWithAccentsRemainReadable()
    {
        var segment = new string('é', 48);
        var folder = Path.Combine(_root, segment, segment, segment, segment, segment);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "vidéo avec espaces.mp4");
        File.WriteAllText(file, "");
        Assert.True(file.Length > 260);
        Assert.Equal(new[] { file, file }, FileQueuePathExpander.Enumerate([folder, file], CancellationToken.None));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
