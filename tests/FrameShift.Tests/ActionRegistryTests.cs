using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ActionRegistryTests
{
    [Fact]
    public void CreateDefault_ContainsAddSubtitlesToVideoAction()
    {
        var registry = ActionRegistry.CreateDefault();

        var found = registry.TryGet("add-subtitles-video", out var action);

        Assert.True(found);
        Assert.NotNull(action);
        Assert.Equal("add-subtitles-video", action.Descriptor.Id);
        Assert.Equal("Add Subtitles to Video", action.Descriptor.DisplayName);
    }
}
