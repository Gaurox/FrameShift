using System.Linq;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class InterpolateVideoPlannerTests
{
    [Fact]
    public void BuildPlan_UsesNvencForMp4WhenAvailable()
    {
        var plan = InterpolateVideoPlanner.BuildPlan(".mp4", nvencAvailable: true);

        Assert.Equal("GPU", plan.ModeLabel);
        Assert.Equal("h264_nvenc", plan.VideoCodec);
        Assert.Contains("-cq", plan.VideoArgs);
    }

    [Fact]
    public void BuildPlan_FallsBackToCpuForWebmEvenWhenNvencIsAvailable()
    {
        var plan = InterpolateVideoPlanner.BuildPlan(".webm", nvencAvailable: true);

        Assert.Equal("CPU", plan.ModeLabel);
        Assert.Equal("libvpx-vp9", plan.VideoCodec);
    }

    [Fact]
    public void BuildArguments_AlwaysKeepsCpuInterpolationFilterAndAudioCopy()
    {
        var settings = new InterpolateVideoSettings(59.94);
        var plan = InterpolateVideoPlanner.BuildPlan(".mp4", nvencAvailable: true);

        var args = InterpolateVideoPlanner.BuildArguments(
            @"E:\input video.mp4",
            @"E:\output video.mp4",
            settings,
            plan);

        Assert.Contains("-vf", args);
        Assert.Contains("minterpolate=fps=59.94", args);
        Assert.Equal("-c:a", args[^3]);
        Assert.Equal("copy", args[^2]);
        Assert.Equal(@"E:\output video.mp4", args[^1]);
        Assert.DoesNotContain(args, value => value.Contains("cuda", System.StringComparison.OrdinalIgnoreCase));
    }
}
