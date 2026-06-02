using System.Collections.Generic;
using FrameShift.Core.Actions;
using FrameShift.Windows.Batch;
using Xunit;

namespace FrameShift.Tests;

public sealed class ConversionBatchQueueMessageTests
{
    // Wire format: path + per-launch options round-trip over the pipe.

    [Fact]
    public void FormatThenParse_WithModelOption_RoundTripsPathAndOptions()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "bria-high-quality"
        };

        var line = ConversionBatchSession.FormatQueueMessage(@"C:\images\photo.png", options);
        var parsed = ConversionBatchSession.TryParseQueueMessage(line, out var path, out var parsedOptions);

        Assert.True(parsed);
        Assert.Equal(@"C:\images\photo.png", path);
        Assert.NotNull(parsedOptions);
        Assert.Equal("bria-high-quality", parsedOptions![ActionOptionKeys.BackgroundRemovalModel]);
    }

    [Fact]
    public void FormatThenParse_OptionsAreCaseInsensitive()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "fast"
        };

        var line = ConversionBatchSession.FormatQueueMessage(@"C:\images\photo.png", options);
        ConversionBatchSession.TryParseQueueMessage(line, out _, out var parsedOptions);

        Assert.NotNull(parsedOptions);
        // Key lookup must ignore case to match the rest of the option dictionaries.
        Assert.True(parsedOptions!.ContainsKey(ActionOptionKeys.BackgroundRemovalModel.ToUpperInvariant()));
    }

    [Fact]
    public void FormatThenParse_NoOptions_ParsesPathWithNullOptions()
    {
        var line = ConversionBatchSession.FormatQueueMessage(@"C:\images\photo.png", null);
        var parsed = ConversionBatchSession.TryParseQueueMessage(line, out var path, out var parsedOptions);

        Assert.True(parsed);
        Assert.Equal(@"C:\images\photo.png", path);
        Assert.Null(parsedOptions);
    }

    [Fact]
    public void TryParseQueueMessage_BarePathLine_IsBackwardCompatible()
    {
        // Older callers (or any plain-path producer) send just the path.
        var parsed = ConversionBatchSession.TryParseQueueMessage(@"C:\images\legacy.png", out var path, out var parsedOptions);

        Assert.True(parsed);
        Assert.Equal(@"C:\images\legacy.png", path);
        Assert.Null(parsedOptions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseQueueMessage_EmptyLine_ReturnsFalse(string? line)
    {
        var parsed = ConversionBatchSession.TryParseQueueMessage(line, out var path, out var parsedOptions);

        Assert.False(parsed);
        Assert.Equal(string.Empty, path);
        Assert.Null(parsedOptions);
    }

    // Merge: per-item options override the session's shared options.

    [Fact]
    public void MergeOptions_ItemModelWinsOverSharedModel()
    {
        var shared = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "fast"
        };
        var item = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "bria-high-quality"
        };

        var merged = ConversionBatchSession.MergeOptions(shared, item);

        Assert.Equal("bria-high-quality", merged[ActionOptionKeys.BackgroundRemovalModel]);
    }

    [Fact]
    public void MergeOptions_NullItemOptions_KeepsSharedOptions()
    {
        var shared = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "fast"
        };

        var merged = ConversionBatchSession.MergeOptions(shared, null);

        Assert.Equal("fast", merged[ActionOptionKeys.BackgroundRemovalModel]);
    }

    [Fact]
    public void MergeOptions_PreservesSharedKeysNotInItem()
    {
        var shared = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "fast",
            [ActionOptionKeys.Profile] = "default"
        };
        var item = new Dictionary<string, string>
        {
            [ActionOptionKeys.BackgroundRemovalModel] = "high-resolution"
        };

        var merged = ConversionBatchSession.MergeOptions(shared, item);

        Assert.Equal("high-resolution", merged[ActionOptionKeys.BackgroundRemovalModel]);
        Assert.Equal("default", merged[ActionOptionKeys.Profile]);
    }
}
