using System.Collections.Generic;
using FrameShift.Core.AI.Upscale;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class UpscaleVideoSettingsTests
{
    [Theory]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    public void TryFromOptions_AcceptsSupportedScale(string value, int expected)
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.UpscaleModel] = "realesr-general-x4v3",
            [ActionOptionKeys.UpscaleScale] = value
        };

        var success = UpscaleVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(settings);
        Assert.Equal(expected, settings!.ScaleFactor);
        Assert.Equal($"_upscaled_{expected}x", settings.OutputSuffix);
    }

    [Fact]
    public void TryFromOptions_UsesVideoDefaultsWhenOptionsAreAbsent()
    {
        var success = UpscaleVideoSettings.TryFromOptions(null, out var settings, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("realesr-general-x4v3", settings!.ModelId);
        Assert.Equal(4, settings.ScaleFactor);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("invalid")]
    public void TryFromOptions_RejectsUnsupportedScale(string value)
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.UpscaleScale] = value
        };

        var success = UpscaleVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.False(success);
        Assert.Null(settings);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryFromOptions_AcceptsPairedCustomTarget()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.UpscaleModel] = "realesr-animevideov3",
            [ActionOptionKeys.UpscaleTargetWidth] = "3840",
            [ActionOptionKeys.UpscaleTargetHeight] = "2160"
        };

        var success = UpscaleVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(3840, settings!.TargetWidth);
        Assert.Equal(2160, settings.TargetHeight);
        Assert.Equal("_upscaled_3840x2160", settings.OutputSuffix);
    }

    [Fact]
    public void TryFromOptions_RejectsUnpairedCustomTarget()
    {
        var options = new Dictionary<string, string>
        {
            [ActionOptionKeys.UpscaleTargetWidth] = "3840"
        };

        var success = UpscaleVideoSettings.TryFromOptions(options, out var settings, out var error);

        Assert.False(success);
        Assert.Null(settings);
        Assert.NotNull(error);
    }

    [Fact]
    public void Catalog_PreservesImagePickerModels_AndPinsVideoChecksums()
    {
        Assert.Equal(3, UpscaleModelCatalog.GetImageModels().Count);
        Assert.Equal("realesrgan-x4plus", UpscaleModelCatalog.GetDefault().Id);
        Assert.Equal("realesr-general-x4v3", UpscaleModelCatalog.GetDefaultVideo().Id);
        Assert.Equal(3, UpscaleModelCatalog.GetVideoModels().Count);

        foreach (var model in UpscaleModelCatalog.GetImageModels())
        {
            Assert.Equal("upscale-image-onnx", model.Folder);
            Assert.Contains("/upscale-image-onnx/", model.DownloadUrl);
            Assert.Equal("upscale-onnx", model.LegacyFolder);
        }

        foreach (var model in UpscaleModelCatalog.GetVideoModels())
        {
            Assert.Equal("upscale-video-onnx", model.Folder);
            Assert.Contains("/upscale-video-onnx/", model.DownloadUrl);
            Assert.Equal("upscale-onnx", model.LegacyFolder);
            Assert.False(UpscaleModelCatalog.IsSha256Placeholder(model.ExpectedSha256));
            Assert.Matches("^[0-9A-F]{64}$", model.ExpectedSha256);
            Assert.True(model.ExpectedSizeBytes > 0);
        }

        Assert.Contains(
            UpscaleModelCatalog.GetVideoModels(),
            model => model.Id == "realesrgan-x4plus-video");
        Assert.DoesNotContain(
            UpscaleModelCatalog.GetVideoModels(),
            model => model.Id == "realesrgan-x4plus");
    }
}
