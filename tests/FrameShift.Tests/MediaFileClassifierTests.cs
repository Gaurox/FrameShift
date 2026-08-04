using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class MediaFileClassifierTests
{
    [Theory]
    [InlineData(@"C:\clips\a.mp4", MediaFamily.Video)]
    [InlineData(@"C:\clips\a.MKV", MediaFamily.Video)]
    [InlineData(@"song.mp3", MediaFamily.Audio)]
    [InlineData(@"voice.WAV", MediaFamily.Audio)]
    [InlineData(@"photo.png", MediaFamily.Image)]
    [InlineData(@"scan.JPEG", MediaFamily.Image)]
    [InlineData(@"notes.txt", MediaFamily.Other)]
    [InlineData(@"archive.zip", MediaFamily.Other)]
    public void Classify_Path_ReturnsExpectedKind(string path, MediaFamily expected)
    {
        Assert.Equal(expected, MediaFileClassifier.Classify(path));
    }

    [Theory]
    [InlineData(".mp4", MediaFamily.Video)]
    [InlineData("mp4", MediaFamily.Video)]
    [InlineData(".MP3", MediaFamily.Audio)]
    [InlineData("webp", MediaFamily.Image)]
    [InlineData(".xyz", MediaFamily.Other)]
    public void ClassifyExtension_LeadingDotOptional_AndCaseInsensitive(string extension, MediaFamily expected)
    {
        Assert.Equal(expected, MediaFileClassifier.ClassifyExtension(extension));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classify_BlankInput_IsOther(string? input)
    {
        Assert.Equal(MediaFamily.Other, MediaFileClassifier.Classify(input!));
    }

    [Fact]
    public void Classification_IsConsistentWithCatalogCategories()
    {
        foreach (var entry in ActionCatalog.Entries)
        {
            var expected = entry.Category switch
            {
                ActionCategory.Video => (MediaFamily?)MediaFamily.Video,
                ActionCategory.Audio => MediaFamily.Audio,
                ActionCategory.Image => MediaFamily.Image,
                _ => null // General (media-info) spans families — not tied to one kind.
            };

            if (expected is null)
            {
                continue;
            }

            foreach (var ext in entry.AcceptedExtensions)
            {
                Assert.Equal(expected.Value, MediaFileClassifier.ClassifyExtension(ext));
            }
        }
    }
}
