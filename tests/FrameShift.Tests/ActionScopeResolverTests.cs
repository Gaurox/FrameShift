using System;
using System.Linq;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class ActionScopeResolverTests
{
    private static readonly string[] Empty = Array.Empty<string>();

    private static ActionAvailability? Find(System.Collections.Generic.IReadOnlyList<ActionAvailability> list, string key)
        => list.FirstOrDefault(a => a.Entry.Key == key);

    [Fact]
    public void ResolveScope_NoSelection_UsesWholeQueue()
    {
        var all = new[] { @"C:\a.mp4", @"C:\b.png" };

        var scope = ActionScopeResolver.ResolveScope(all, Empty, null);

        Assert.Equal(all, scope);
    }

    [Fact]
    public void ResolveScope_WithSelection_UsesSelection()
    {
        var all = new[] { @"C:\a.mp4", @"C:\b.png", @"C:\c.png" };
        var selected = new[] { @"C:\b.png" };

        var scope = ActionScopeResolver.ResolveScope(all, selected, null);

        Assert.Equal(selected, scope);
    }

    [Fact]
    public void ResolveScope_FamilyFilter_RestrictsToFamily()
    {
        var all = new[] { @"C:\a.mp4", @"C:\b.png", @"C:\c.png", @"C:\d.mp3" };

        var scope = ActionScopeResolver.ResolveScope(all, Empty, MediaFamily.Image);

        Assert.Equal(new[] { @"C:\b.png", @"C:\c.png" }, scope);
    }

    [Fact]
    public void Resolve_Union_IncludesActionsForEveryPresentType_ExcludesOthers()
    {
        var scope = new[] { @"C:\a.mp4", @"C:\b.png" };

        var result = ActionScopeResolver.Resolve(scope);
        var keys = result.Select(a => a.Entry.Key).ToArray();

        Assert.Contains("convert-video", keys);
        Assert.Contains("convert-image", keys);
        Assert.DoesNotContain("convert-audio", keys); // no audio file in scope
    }

    [Fact]
    public void Resolve_BadgeCounts_ReflectMatchingSubset()
    {
        var scope = new[] { @"C:\a.mp4", @"C:\b.mov", @"C:\c.png", @"C:\d.png", @"C:\e.png" };

        var result = ActionScopeResolver.Resolve(scope);

        Assert.Equal(2, Find(result, "convert-video")!.MatchingFileCount);
        Assert.Equal(3, Find(result, "convert-image")!.MatchingFileCount);
        Assert.Equal(3, Find(result, "remove-background-fast")!.MatchingFileCount);
    }

    [Fact]
    public void Resolve_SingleAction_DisabledWhenMultipleMatch()
    {
        var scope = new[] { @"C:\a.png", @"C:\b.png", @"C:\c.png" };

        var crop = Find(ActionScopeResolver.Resolve(scope), "crop-image")!;

        Assert.False(crop.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(crop.DisabledReason));
        Assert.Equal(3, crop.MatchingFileCount);
    }

    [Fact]
    public void Resolve_SingleAction_EnabledWhenExactlyOne()
    {
        var scope = new[] { @"C:\a.png" };

        var crop = Find(ActionScopeResolver.Resolve(scope), "crop-image")!;

        Assert.True(crop.IsEnabled);
        Assert.Null(crop.DisabledReason);
    }

    [Fact]
    public void Resolve_BatchAction_EnabledWithMultiple()
    {
        var scope = new[] { @"C:\a.png", @"C:\b.png", @"C:\c.png" };

        var upscale = Find(ActionScopeResolver.Resolve(scope), "upscale-image")!;

        Assert.True(upscale.IsEnabled);
        Assert.Equal(3, upscale.MatchingFileCount);
    }

    [Fact]
    public void Resolve_CombineAction_EnabledWithMultiple()
    {
        var scope = new[] { @"C:\a.png", @"C:\b.png", @"C:\c.png" };

        var pdf = Find(ActionScopeResolver.Resolve(scope), "image-to-pdf")!;

        Assert.True(pdf.IsEnabled);
        Assert.Equal(3, pdf.MatchingFileCount);
    }

    [Fact]
    public void Resolve_MediaInfo_AppearsForAnyFamily()
    {
        Assert.NotNull(Find(ActionScopeResolver.Resolve(new[] { @"C:\a.mp4" }), "media-info"));
        Assert.NotNull(Find(ActionScopeResolver.Resolve(new[] { @"C:\a.mp3" }), "media-info"));
        Assert.NotNull(Find(ActionScopeResolver.Resolve(new[] { @"C:\a.png" }), "media-info"));
    }

    [Fact]
    public void Resolve_EmptyScope_ReturnsEmpty()
    {
        Assert.Empty(ActionScopeResolver.Resolve(Empty));
    }

    [Fact]
    public void MatchingFiles_ReturnsOnlyAcceptedFiles()
    {
        var scope = new[] { @"C:\a.mp4", @"C:\b.png", @"C:\c.png" };

        Assert.True(ActionCatalog.TryGet("convert-image", out var convertImage));
        var files = ActionScopeResolver.MatchingFiles(convertImage, scope);

        Assert.Equal(new[] { @"C:\b.png", @"C:\c.png" }, files);
    }

    [Fact]
    public void FamilyCounts_CountsPerFamily()
    {
        var files = new[] { @"C:\a.mp4", @"C:\b.mov", @"C:\c.png", @"C:\d.mp3" };

        var counts = ActionScopeResolver.FamilyCounts(files);

        Assert.Equal(2, counts[MediaFamily.Video]);
        Assert.Equal(1, counts[MediaFamily.Image]);
        Assert.Equal(1, counts[MediaFamily.Audio]);
    }

    [Fact]
    public void Resolve_PreservesCategoryGrouping_ContiguousByCategory()
    {
        var scope = new[] { @"C:\a.mp4", @"C:\b.mp3", @"C:\c.png" };

        var categories = ActionScopeResolver.Resolve(scope)
            .Select(a => a.Entry.Category)
            .ToArray();

        // Each category should appear as one contiguous block (no interleaving).
        var seen = new System.Collections.Generic.List<ActionCategory>();
        ActionCategory? previous = null;
        foreach (var category in categories)
        {
            if (category != previous)
            {
                Assert.DoesNotContain(category, seen);
                seen.Add(category);
                previous = category;
            }
        }
    }
}
