using System.Collections.Generic;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ProgramBatchTests
{
    [Fact]
    public void ShouldRunConversionBatch_ExtractFramesWithMode_RemainsInTheSharedQueue()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.FrameMode] = "last"
        };

        Assert.True(Program.ShouldRunConversionBatch("extract-frames", options));
    }

    [Fact]
    public void ShouldRunConversionBatch_OtherConfiguredConversions_KeepTheirExistingDirectRouting()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.Target] = "mkv"
        };

        Assert.False(Program.ShouldRunConversionBatch("convert-video", options));
    }

    [Theory]
    [InlineData("compress-video", ActionOptionKeys.Profile)]
    [InlineData("compress-audio", ActionOptionKeys.Profile)]
    [InlineData("compress-image", ActionOptionKeys.TargetSizeBytes)]
    public void HeadlessCompressionDetection_RemainsUnchanged(string actionId, string optionKey)
    {
        var options = new Dictionary<string, string>
        {
            [optionKey] = "value"
        };

        Assert.True(Program.IsHeadlessCompressionInvocation(actionId, options));
        Assert.False(Program.IsHeadlessCompressionInvocation(actionId, new Dictionary<string, string>()));
    }
}
