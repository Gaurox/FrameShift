using System;
using System.Linq;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

// Structural guards for the central action catalog consumed by the main window.
// These lock the routing metadata (ids, categories, arity, extensions, CLI args)
// so accidental drift is caught, and cross-check the convert-* extension sets
// against the existing public conversion catalogs.
public sealed class ActionCatalogTests
{
    [Fact]
    public void Entries_AreNotEmpty()
    {
        Assert.NotEmpty(ActionCatalog.Entries);
    }

    [Fact]
    public void Keys_AreUnique_CaseInsensitive()
    {
        var duplicates = ActionCatalog.Entries
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryEntry_HasValidShape()
    {
        foreach (var entry in ActionCatalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key));
            Assert.False(string.IsNullOrWhiteSpace(entry.ActionId));
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
            Assert.NotEmpty(entry.AcceptedExtensions);
        }
    }

    [Fact]
    public void AllExtensions_AreLowercase_WithLeadingDot()
    {
        foreach (var entry in ActionCatalog.Entries)
        {
            foreach (var ext in entry.AcceptedExtensions)
            {
                Assert.StartsWith(".", ext, StringComparison.Ordinal);
                Assert.Equal(ext.ToLowerInvariant(), ext);
            }
        }
    }

    [Fact]
    public void ExtraCliArgs_AreEmpty_ExceptRemoveBackground()
    {
        foreach (var entry in ActionCatalog.Entries)
        {
            if (string.Equals(entry.ActionId, "remove-background", StringComparison.OrdinalIgnoreCase))
            {
                Assert.NotEmpty(entry.ExtraCliArgs);
            }
            else
            {
                Assert.Empty(entry.ExtraCliArgs);
            }
        }
    }

    [Fact]
    public void RemoveBackground_HasFiveVariants_WithModelFlags()
    {
        var variants = ActionCatalog.Entries
            .Where(entry => string.Equals(entry.ActionId, "remove-background", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(5, variants.Length);

        foreach (var variant in variants)
        {
            Assert.Equal(ActionCategory.Image, variant.Category);
            Assert.Equal(ActionArity.Batch, variant.Arity);
            Assert.True(variant.Accepts(".png"));
            Assert.Equal(2, variant.ExtraCliArgs.Count);
            Assert.Equal("--rmbg-model", variant.ExtraCliArgs[0]);
        }

        var modelIds = variants.Select(variant => variant.ExtraCliArgs[1]).ToArray();
        Assert.Equal(modelIds.Length, modelIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            new[] { "fast", "high-resolution", "high-resolution-general", "bria-balanced", "bria-high-quality" }
                .OrderBy(id => id, StringComparer.Ordinal),
            modelIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("convert-video", ActionArity.Batch, ActionCategory.Video)]
    [InlineData("upscale-image", ActionArity.Batch, ActionCategory.Image)]
    [InlineData("crop-image", ActionArity.Single, ActionCategory.Image)]
    [InlineData("cut-video", ActionArity.Single, ActionCategory.Video)]
    [InlineData("image-to-pdf", ActionArity.Combine, ActionCategory.Image)]
    [InlineData("media-info", ActionArity.Single, ActionCategory.General)]
    public void KnownEntries_HaveExpectedArityAndCategory(string key, ActionArity arity, ActionCategory category)
    {
        Assert.True(ActionCatalog.TryGet(key, out var entry));
        Assert.Equal(arity, entry.Arity);
        Assert.Equal(category, entry.Category);
    }

    [Fact]
    public void Accepts_IsCaseInsensitive_AndDotOptional()
    {
        Assert.True(ActionCatalog.TryGet("convert-video", out var convertVideo));

        Assert.True(convertVideo.Accepts(".mp4"));
        Assert.True(convertVideo.Accepts(".MP4"));
        Assert.True(convertVideo.Accepts("mp4"));
        Assert.False(convertVideo.Accepts(".png"));
        Assert.False(convertVideo.Accepts(""));
    }

    [Fact]
    public void ConvertVideo_Extensions_MatchVideoConversionCatalog()
    {
        Assert.True(ActionCatalog.TryGet("convert-video", out var entry));
        Assert.All(entry.AcceptedExtensions, ext => Assert.True(VideoConversionCatalog.IsSupportedSourceExtension(ext)));
        Assert.Equal(6, entry.AcceptedExtensions.Count);
    }

    [Fact]
    public void ConvertAudio_Extensions_MatchAudioConversionCatalog()
    {
        Assert.True(ActionCatalog.TryGet("convert-audio", out var entry));
        Assert.All(entry.AcceptedExtensions, ext => Assert.True(AudioConversionCatalog.IsSupportedSourceExtension(ext)));
        Assert.DoesNotContain(".aac", entry.AcceptedExtensions);
        Assert.DoesNotContain(".wma", entry.AcceptedExtensions);
    }

    [Fact]
    public void ConvertImage_Extensions_MatchImageConversionCatalog()
    {
        Assert.True(ActionCatalog.TryGet("convert-image", out var entry));
        Assert.All(entry.AcceptedExtensions, ext => Assert.True(ImageConversionCatalog.IsSupportedSourceExtension(ext)));
        Assert.Equal(5, entry.AcceptedExtensions.Count);
    }

    [Fact]
    public void ForExtension_Png_IncludesImageActions_ExcludesVideo()
    {
        var keys = ActionCatalog.ForExtension(".png").Select(entry => entry.Key).ToArray();

        Assert.Contains("convert-image", keys);
        Assert.Contains("upscale-image", keys);
        Assert.Contains("remove-background-fast", keys);
        Assert.Contains("image-to-pdf", keys);
        Assert.Contains("media-info", keys);
        Assert.DoesNotContain("convert-video", keys);
        Assert.DoesNotContain("convert-audio", keys);
    }

    [Fact]
    public void ForExtension_Mp3_IncludesAudioActions_ExcludesImage()
    {
        var keys = ActionCatalog.ForExtension(".mp3").Select(entry => entry.Key).ToArray();

        Assert.Contains("convert-audio", keys);
        Assert.Contains("separate-audio", keys);
        Assert.Contains("media-info", keys);
        Assert.DoesNotContain("convert-image", keys);
    }

    [Fact]
    public void SeparateAudio_And_RemoveNoise_HaveDistinctNarrowerSets()
    {
        Assert.True(ActionCatalog.TryGet("separate-audio", out var separate));
        Assert.True(separate.Accepts(".aac"));
        Assert.False(separate.Accepts(".wave"));

        Assert.True(ActionCatalog.TryGet("remove-noise", out var noise));
        Assert.False(noise.Accepts(".aac"));
        Assert.False(noise.Accepts(".wma"));
        Assert.True(noise.Accepts(".wav"));
    }

    [Fact]
    public void MediaInfo_Accepts_AllThreeFamilies()
    {
        Assert.True(ActionCatalog.TryGet("media-info", out var entry));
        Assert.True(entry.Accepts(".mp4"));
        Assert.True(entry.Accepts(".mp3"));
        Assert.True(entry.Accepts(".png"));
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalse()
    {
        Assert.False(ActionCatalog.TryGet("does-not-exist", out _));
    }

    [Fact]
    public void ExtractFrames_UsesTheAllFramesHubLabelWithoutExtraArguments()
    {
        Assert.True(ActionCatalog.TryGet("extract-frames", out var entry));

        Assert.Equal("Extract all frames", entry.DisplayName);
        Assert.Empty(entry.ExtraCliArgs);
    }
}
