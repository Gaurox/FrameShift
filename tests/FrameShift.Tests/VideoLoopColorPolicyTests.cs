using FrameShift.Core.Actions;
using FrameShift.Core.FFprobe;
using Xunit;

namespace FrameShift.Tests;

public sealed class VideoLoopColorPolicyTests
{
    internal static MediaProbeResult Probe(string? transfer = "bt709", string? primaries = "bt709",
        string? pixelFormat = "yuv420p", int? bits = 8, string? profile = null, string? space = "bt709")
        => new(TimeSpan.FromSeconds(5), false, true, 160, 96, "hevc", 30, 150, [], [], [])
        {
            VideoColorTransfer = transfer, VideoColorPrimaries = primaries, VideoColorSpace = space,
            VideoPixelFormat = pixelFormat, VideoBitDepth = bits, VideoProfile = profile
        };

    [Theory]
    [InlineData("yuv420p10le", 10, "Main 10")]
    [InlineData("yuv444p12le", 12, null)]
    [InlineData("p010le", 10, "Main10")]
    public void ExplicitSdrHighBitDepth_IsSupportedWithoutChangingOtherActions(string format, int bits, string? profile)
    {
        var probe = Probe(pixelFormat: format, bits: bits, profile: profile);
        Assert.True(probe.IsHdrLikely); // The shared historical policy is deliberately unchanged.
        var policy = VideoLoopColorPolicy.Evaluate(probe);
        Assert.Equal(VideoLoopColorKind.Sdr, policy.Kind);
        Assert.True(policy.CanRender);
        policy.RequireSupported();
    }

    [Theory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    [InlineData(" SMPTE2084 ")]
    public void PqAndHlg_AreConfirmedHdrAndUseLinearLightToneMapping(string transfer)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(transfer, "bt2020", "yuv420p10le", 10, space: "bt2020nc"));
        Assert.Equal(VideoLoopColorKind.Hdr, policy.Kind);
        Assert.True(policy.CanRender);
        Assert.True(policy.ToneMapsHdr);
        Assert.Contains("HDR source → SDR output", policy.Message);
        Assert.Contains("t=linear:npl=100", policy.Filter);
        Assert.Contains("format=gbrpf32le", policy.Filter);
        Assert.Contains("tonemap=tonemap=hable", policy.Filter);
    }

    [Theory]
    [InlineData("bt709")]
    [InlineData("bt2020-10")]
    [InlineData("bt2020-12")]
    public void ExplicitWideGamutSdr_IsNotCalledHdr(string transfer)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(transfer, "bt2020", "yuv420p10le", 10, space: "bt2020nc"));
        Assert.Equal(VideoLoopColorKind.Sdr, policy.Kind);
        Assert.True(policy.CanRender);
        Assert.Contains("wide-gamut", policy.Message);
        Assert.DoesNotContain("tonemap=", policy.Filter);
    }

    [Theory]
    [InlineData("yuv420p10le", null, null)]
    [InlineData("yuv444p12le", null, null)]
    [InlineData("p010le", null, null)]
    [InlineData("gray16le", null, null)]
    [InlineData("rgb48le", null, null)]
    [InlineData("yuv420p", 10, null)]
    [InlineData("yuv420p", null, "Main 10")]
    [InlineData("yuv420p", null, "Main10")]
    public void MissingHighBitDepthTransfer_RemainsUndetermined(string format, int? bits, string? profile)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(null, pixelFormat: format, bits: bits, profile: profile));
        Assert.Equal(VideoLoopColorKind.Undetermined, policy.Kind);
        Assert.False(policy.CanRender);
        Assert.Contains("metadata", policy.Message);
    }

    [Theory]
    [InlineData("yuv420p", null)]
    [InlineData("yuv410p", "unknown")]
    [InlineData("rgb24", "unspecified")]
    public void UntaggedOrdinaryVideo_KeepsSdrCompatibilityWithoutClaimingConfirmedSdr(string format, string? transfer)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(transfer, pixelFormat: format, bits: null));
        Assert.Equal(VideoLoopColorKind.Undetermined, policy.Kind);
        Assert.True(policy.CanRender);
    }

    [Theory]
    [InlineData("linear", "bt709", "bt709")]
    [InlineData("log100", "bt709", "bt709")]
    [InlineData(null, "bt2020", "bt2020nc")]
    public void UnsupportedOrMissingColorInformation_IsReportedBeforeRendering(string? transfer, string primaries, string space)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(transfer, primaries, space: space));
        Assert.Equal(VideoLoopColorKind.Undetermined, policy.Kind);
        Assert.False(policy.CanRender);
    }

    [Theory]
    [InlineData("ictcp")]
    [InlineData("chroma-derived-nc")]
    public void ExplicitSdrWithUnsupportedMatrix_DoesNotBypassColorValidation(string space)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(space: space));
        Assert.Equal(VideoLoopColorKind.Undetermined, policy.Kind);
        Assert.False(policy.CanRender);
        Assert.Contains("matrix", policy.Message);
    }

    [Fact]
    public void Bt2020TransferWithMissingGamut_StillRequiresTheDedicatedConversion()
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe("bt2020-10", null, space: null));
        Assert.Equal(VideoLoopColorKind.Sdr, policy.Kind);
        Assert.False(policy.CanRender);
    }

    [Theory]
    [InlineData("smpte2084", null, "bt2020nc")]
    [InlineData("arib-std-b67", "bt2020", null)]
    [InlineData("smpte2084", "bt709", "bt709")]
    public void HdrMissingOrInconsistentGamut_IsExplicitlyBlocked(string transfer, string? primaries, string? space)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe(transfer, primaries, space: space));
        Assert.Equal(VideoLoopColorKind.Hdr, policy.Kind);
        Assert.False(policy.CanRender);
        Assert.Contains("metadata", policy.Message);
    }

    [Fact]
    public void PqPeakAndRange_AreExplicit_AndRequiredFiltersCannotBeSilentlyOmitted()
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe("smpte2084", "bt2020", space: "bt2020nc") with
        { VideoColorRange = "pc", VideoPeakLuminanceNits = 4000 });
        Assert.Contains("rin=full", policy.Filter);
        Assert.Contains("peak=40", policy.Filter);
        Assert.DoesNotContain("fps=", policy.Filter);
        Assert.DoesNotContain("setpts=", policy.Filter);
        foreach (var required in new[] { "zscale", "tonemap", "sidedata", "setparams" })
        {
            var filters = new HashSet<string>(["zscale", "tonemap", "sidedata", "setparams"]);
            filters.Remove(required);
            Assert.Contains(required, Assert.Throws<InvalidOperationException>(() => policy.RequireFilters(filters)).Message);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20000)]
    [InlineData(double.NaN)]
    public void InvalidPeak_IsReplacedByTheAnnouncedNominalFallback(double peak)
    {
        var policy = VideoLoopColorPolicy.Evaluate(Probe("smpte2084", "bt2020", space: "bt2020nc") with { VideoPeakLuminanceNits = peak });
        Assert.Contains("fallback", policy.Message);
        Assert.Contains("peak=10,", policy.Filter);
    }
}
