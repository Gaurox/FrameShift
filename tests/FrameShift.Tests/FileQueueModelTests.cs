using System;
using FrameShift.Core.Actions;
using Xunit;

namespace FrameShift.Tests;

public sealed class FileQueueModelTests
{
    [Fact]
    public void Add_NewPath_ReturnsTrue_AndStores()
    {
        var model = new FileQueueModel();

        Assert.True(model.Add(@"C:\a.mp4"));
        Assert.Equal(1, model.Count);
        Assert.True(model.Contains(@"C:\a.mp4"));
    }

    [Fact]
    public void Add_Duplicate_IsRejected_CaseInsensitive()
    {
        var model = new FileQueueModel();
        model.Add(@"C:\Video\Clip.mp4");

        Assert.False(model.Add(@"c:\video\clip.MP4"));
        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void Add_BlankOrNull_IsRejected()
    {
        var model = new FileQueueModel();

        Assert.False(model.Add(""));
        Assert.False(model.Add("   "));
        Assert.False(model.Add(null!));
        Assert.Equal(0, model.Count);
    }

    [Fact]
    public void Items_PreserveInsertionOrder()
    {
        var model = new FileQueueModel();
        model.Add(@"C:\b.mp4");
        model.Add(@"C:\a.mp4");
        model.Add(@"C:\c.mp4");

        Assert.Equal(new[] { @"C:\b.mp4", @"C:\a.mp4", @"C:\c.mp4" }, model.Items);
    }

    [Fact]
    public void AddRange_ReturnsNewlyAddedCount_SkippingDuplicatesAndBlanks()
    {
        var model = new FileQueueModel();
        model.Add(@"C:\a.mp4");

        var added = model.AddRange(new[] { @"C:\a.mp4", @"C:\b.mp4", "  ", @"C:\c.mp4" });

        Assert.Equal(2, added);
        Assert.Equal(3, model.Count);
    }

    [Fact]
    public void AddRange_Null_Throws()
    {
        var model = new FileQueueModel();
        Assert.Throws<ArgumentNullException>(() => model.AddRange(null!));
    }

    [Fact]
    public void Remove_CaseInsensitive_RemovesItem()
    {
        var model = new FileQueueModel();
        model.Add(@"C:\Video\Clip.mp4");

        Assert.True(model.Remove(@"c:\video\clip.mp4"));
        Assert.Equal(0, model.Count);
        Assert.False(model.Contains(@"C:\Video\Clip.mp4"));
    }

    [Fact]
    public void Remove_Absent_ReturnsFalse()
    {
        var model = new FileQueueModel();
        model.Add(@"C:\a.mp4");

        Assert.False(model.Remove(@"C:\missing.mp4"));
        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void Remove_ThenReAdd_IsAllowed()
    {
        var model = new FileQueueModel();
        model.Add(@"C:\a.mp4");
        model.Remove(@"C:\a.mp4");

        Assert.True(model.Add(@"C:\a.mp4"));
        Assert.Equal(1, model.Count);
    }

    [Fact]
    public void Clear_EmptiesQueue()
    {
        var model = new FileQueueModel();
        model.AddRange(new[] { @"C:\a.mp4", @"C:\b.mp3" });

        model.Clear();

        Assert.Equal(0, model.Count);
        Assert.False(model.Contains(@"C:\a.mp4"));
    }

    [Fact]
    public void CountByKind_CountsPerFamily()
    {
        var model = new FileQueueModel();
        model.AddRange(new[] { @"C:\a.mp4", @"C:\b.mov", @"C:\c.png", @"C:\d.mp3", @"C:\e.txt" });

        var counts = model.CountByKind();

        Assert.Equal(2, counts[MediaFamily.Video]);
        Assert.Equal(1, counts[MediaFamily.Image]);
        Assert.Equal(1, counts[MediaFamily.Audio]);
        Assert.Equal(1, counts[MediaFamily.Other]);
    }
}
